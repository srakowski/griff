using System.Diagnostics;
using System.Runtime.InteropServices;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Rewind for play mode: keeps periodic snapshots of the running scene so you can step back in time, inspect or tweak,
/// and resume from there. A snapshot holds what a scene file does (public script properties included; private script
/// fields restart from their defaults) and restores as the file would load, but is kept in a compact binary form made
/// without garbage. Taking one is split in two so the game's steps pay for as little of it as they can: between two steps,
/// every entity's values are copied out at once (its record), which is little more than reading them; then, on another
/// thread (it reads nothing but the copies), each record is compared with the entity's record in the last snapshot and
/// kept as just the bytes that changed (next to nothing for an entity that didn't change), with every tenth snapshot kept
/// whole. Big scenes are snapshotted less often, so the snapshots stay within <see cref="MemoryBudget"/> and still reach as
/// far back, and (with a <see cref="CostPerStep"/>) so copying them out doesn't slow the game.
/// </summary>
public sealed class Timeline
{
    // Every KeyEvery-th snapshot is kept whole, so restoring one decodes at most this many.
    private const int KeyEvery = 10;

    private const int ChunkSize = 128 * 1024;

    private readonly SnapshotCodec _codec;
    private readonly int _capacity;
    private readonly int _framesPerSnapshot;
    private readonly List<Snapshot> _snapshots = [];

    // Dropped snapshots a kept one is still written against (they go when the oldest kept one is whole).
    private readonly List<Snapshot> _retained = [];

    // The last snapshot's records (what the next one is compared with), and the newest one's while it's settled.
    private Frame _base = new();
    private Frame _current = new();
    private Snapshot? _baseSnapshot;

    // The newest snapshot while it's settled, and the settling (on another thread). Until it's done, only that thread
    // touches _current, _base, _stream and _streamRefs.
    private Snapshot? _pending;
    private Task? _settling;
    private bool _pendingTimed;
    private int _shift;
    private long _pendingStrings;
    private double _pendingSettleMs;
    private readonly ChunkWriter _stream = new();
    private readonly SnapshotBuffer _streamRefs = new();
    private readonly int[] _runs = new int[2 * 255];

    // What physics remembers of a scene, from snapshots that have gone, to use again.
    private readonly Stack<ContactMemory> _contactPool = [];

    private long _sequence;
    private int _stepsSinceSnapshot;
    // The sizes of the last whole snapshot and the last one kept as changes, for the average size.
    private long _wholeBytes;
    private long _changedBytes;
    private double _averageCapture;
    private double _averageSettle;

    /// <param name="capacity">The most snapshots kept: past it, the oldest go.</param>
    /// <param name="framesPerSnapshot">Steps between snapshots (6, ten a second at 60 steps a second) while they're small.</param>
    public Timeline(SceneSerializer serializer, int capacity = 200, int framesPerSnapshot = 6)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSnapshot, 1);
        _codec = new SnapshotCodec(serializer);
        _capacity = capacity;
        _framesPerSnapshot = framesPerSnapshot;
        Spacing = framesPerSnapshot;
    }

    /// <summary>
    /// The most memory the snapshots may take (256 MB unless set). Snapshots of a scene too big for the capacity's worth to
    /// fit are taken less often, so the timeline still reaches back as far; past it, the oldest go.
    /// </summary>
    public long MemoryBudget { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// The most time a step may spend copying snapshots out (<see cref="CaptureTime"/>), on average (none unless set): a
    /// scene that takes longer is snapshotted less often, though at least every <see cref="MostStepsForTime"/> steps. The
    /// editor sets it so a big scene keeps its frame rate while it plays: a snapshot is copied out between two steps, so a
    /// slow one makes that frame late, and few enough of them don't show. (Settling them, <see cref="SettleTime"/>, is
    /// done on another thread.)
    /// </summary>
    public TimeSpan CostPerStep { get; init; }

    /// <summary>The most steps <see cref="CostPerStep"/> spaces snapshots out to: 120, two seconds at 60 steps a second.</summary>
    public const int MostStepsForTime = 120;

    /// <summary>Snapshots kept, the newest included while it's still being settled.</summary>
    public int Count => _snapshots.Count;

    /// <summary>Game time of each snapshot, oldest first.</summary>
    public IEnumerable<double> Times => _snapshots.Select(s => s.Time);

    public double TimeAt(int index) => _snapshots[index].Time;

    /// <summary>Steps between snapshots now: the steps it was made with, or more for a big scene (see <see cref="MemoryBudget"/>).</summary>
    public int Spacing { get; private set; }

    /// <summary>Memory the kept snapshots take, in bytes, once they're settled (see <see cref="Complete"/>).</summary>
    public long Bytes { get; private set; }

    /// <summary>The time copying a snapshot's values out takes, on average: the part a step waits for.</summary>
    public TimeSpan CaptureTime => TimeSpan.FromMilliseconds(_averageCapture);

    /// <summary>The time comparing a snapshot with the last and keeping what changed takes, on average, on another thread.</summary>
    public TimeSpan SettleTime => TimeSpan.FromMilliseconds(_averageSettle);

    /// <summary>
    /// Call once per step of the game. Takes a snapshot every <see cref="Spacing"/> steps (or now, with
    /// <paramref name="force"/>) and drops the oldest past capacity. True if it took one (which is then also the moment
    /// <see cref="Remember"/> keeps).
    /// </summary>
    public bool Record(Scene scene, double time, bool force = false)
    {
        var due = force || ++_stepsSinceSnapshot >= Spacing;
        // The last is kept once it's settled; the next isn't taken until it is.
        if (_settling is not null && (due || _settling.IsCompleted))
            Keep(trim: true);
        if (!due)
            return false;

        _stepsSinceSnapshot = 0;
        var start = Stopwatch.GetTimestamp();
        Capture(scene, time);
        // The first takes longer, with the code that writes snapshots made ready as it goes.
        _pendingTimed = !force;
        if (_pendingTimed)
            _averageCapture = Average(_averageCapture, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        (_remembered, _rememberedCopy) = (_snapshots[^1], false);
        while (_snapshots.Count > _capacity)
            DropOldest();
        _settling = Task.Run(Settle);
        return true;
    }

    // ----- Remembering the moment before a step -----

    // The moment the game's next step starts from: a snapshot, or (with _rememberedCopy) the scene copied out into _spare.
    private Snapshot? _remembered;
    private bool _rememberedCopy;
    private Frame _spare = new();
    private double _spareTime;

    /// <summary>
    /// The most a step may spend copying the scene out for <see cref="Remember"/>, on average (a quarter millisecond unless
    /// set): a scene that takes longer isn't remembered between snapshots, and <see cref="KeepRemembered"/> falls back to
    /// the newest snapshot.
    /// </summary>
    public TimeSpan RememberBudget { get; init; } = TimeSpan.FromMilliseconds(0.25);

    /// <summary>
    /// Remembers the scene as it is between two steps (after a step that took no snapshot), so that if the next step goes
    /// wrong (a script throws), <see cref="KeepRemembered"/> can keep it as a snapshot: the moment just before that step.
    /// It's copied out as a snapshot is, without settling it or keeping it unless asked; a scene too big to copy out every
    /// step (see <see cref="RememberBudget"/>) isn't.
    /// </summary>
    public void Remember(Scene scene, double time)
    {
        (_remembered, _rememberedCopy) = (null, false);
        if (RememberBudget <= TimeSpan.Zero || _averageCapture > RememberBudget.TotalMilliseconds)
            return;
        var start = Stopwatch.GetTimestamp();
        _spare.Clear();
        var roots = scene.Roots;
        for (var i = 0; i < roots.Count; i++)
            Write(_spare, roots[i]);
        (_rememberedCopy, _spareTime) = (true, time);
        _averageCapture = Average(_averageCapture, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>The next step starts from snapshot <paramref name="index"/> (the game was just put back there).</summary>
    public void RememberSnapshot(int index) => (_remembered, _rememberedCopy) = (_snapshots[index], false);

    /// <summary>
    /// The snapshot holding the moment the step under way started from (see <see cref="Remember"/>): one already kept, or
    /// the remembered copy, kept now as the newest snapshot (anything after <paramref name="position"/>, the moment the game
    /// went on from, goes first: it's another future). Its index, and whether it's exactly that moment; without one (a scene
    /// too big to remember every step), the snapshot at <paramref name="position"/>, the last before it. Null if there's none.
    /// </summary>
    public (int Index, bool Exact)? KeepRemembered(int position)
    {
        if (_remembered is { } snapshot && _snapshots.IndexOf(snapshot) is var index and >= 0)
            return (index, true);
        if (!_rememberedCopy)
            return _snapshots.Count == 0 ? null : (Math.Clamp(position, 0, _snapshots.Count - 1), false);

        if (position < _snapshots.Count - 1)
            TruncateAfter(position);
        if (_settling is not null)
            Keep(trim: true);
        var basis = _baseSnapshot is { Chain: < KeyEvery - 1 } last ? last : null;
        var kept = new Snapshot(_spareTime, ++_sequence, basis);
        (_current, _spare) = (_spare, _current);
        kept.Entities = _current.Count;
        _snapshots.Add(kept);
        _pending = kept;
        _pendingTimed = false;
        while (_snapshots.Count > _capacity)
            DropOldest();
        _settling = Task.Run(Settle);
        (_remembered, _rememberedCopy) = (kept, false);
        return (_snapshots.Count - 1, true);
    }

    /// <summary>A number that names snapshot <paramref name="index"/> for as long as it's kept (indices move as the oldest go).</summary>
    public long SequenceAt(int index) => _snapshots[index].Sequence;

    /// <summary>The index of the snapshot <see cref="SequenceAt"/> named <paramref name="sequence"/>, or -1 once it's gone.</summary>
    public int IndexOfSequence(long sequence) => _snapshots.FindIndex(s => s.Sequence == sequence);

    /// <summary>Waits for the newest snapshot to be settled, and keeps it.</summary>
    public void Complete()
    {
        if (_settling is not null)
            Keep(trim: true);
    }

    /// <summary>A fresh scene in the state of snapshot <paramref name="index"/>.</summary>
    public Scene Restore(int index)
    {
        var snapshot = _snapshots[index];
        if (snapshot == _pending)
            Keep(trim: false);

        var frame = snapshot == _baseSnapshot ? _base : Decode(snapshot);
        var reader = new SnapshotReader();
        var seen = new HashSet<Guid>();
        var scene = new Scene();
        var next = 0;
        while (next < frame.Count)
            scene.Add(Read(frame, ref next, reader, seen));
        snapshot.Contacts?.CopyTo(scene.Contacts);
        return scene;
    }

    /// <summary>Forgets everything after <paramref name="index"/>, so resuming from it starts a new future.</summary>
    public void TruncateAfter(int index)
    {
        if (_settling is not null)
            Keep(trim: false);
        while (_snapshots.Count > index + 1)
        {
            var last = _snapshots[^1];
            _snapshots.RemoveAt(_snapshots.Count - 1);
            Bytes -= last.Bytes;
            Return(last);
            // The next snapshot can't be written against one that's gone.
            _baseSnapshot = null;
        }

        _stepsSinceSnapshot = 0;
    }

    public void Clear()
    {
        if (_settling is not null)
            Keep(trim: false);
        _snapshots.Clear();
        _retained.Clear();
        _baseSnapshot = null;
        Bytes = 0;
        _stepsSinceSnapshot = 0;
        (_remembered, _rememberedCopy) = (null, false);
    }

    // Copies every entity's record out, depth first, into the current frame, to be settled on another thread.
    private void Capture(Scene scene, double time)
    {
        var basis = _baseSnapshot is { Chain: < KeyEvery - 1 } last ? last : null;
        var snapshot = new Snapshot(time, ++_sequence, basis);
        _current.Clear();
        var roots = scene.Roots;
        for (var i = 0; i < roots.Count; i++)
            Write(_current, roots[i]);

        snapshot.Entities = _current.Count;
        if (scene.Contacts.Count > 0)
        {
            snapshot.Contacts = _contactPool.Count > 0 ? _contactPool.Pop() : new ContactMemory();
            scene.Contacts.CopyTo(snapshot.Contacts);
        }

        _snapshots.Add(snapshot);
        _pending = snapshot;
    }

    private void Write(Frame frame, Entity entity)
    {
        var buffer = frame.Buffer;
        var offset = buffer.Length;
        var refStart = buffer.RefCount;
        _codec.Write(entity, buffer);
        var children = entity.ChildList;
        frame.Add(new Entry(entity.Id, offset, buffer.Length - offset, refStart, buffer.RefCount - refStart, children.Count));
        for (var i = 0; i < children.Count; i++)
            Write(frame, children[i]);
    }

    // On another thread: works out how the pending snapshot keeps each record, into its chunks and strings.
    private void Settle()
    {
        var start = Stopwatch.GetTimestamp();
        _shift = 0;
        _pendingStrings = 0;
        _stream.Reset();
        _streamRefs.Clear();
        for (var i = 0; i < _current.Count; i++)
            SettleEntry(i);

        var snapshot = _pending!;
        (snapshot.Chunks, snapshot.Used) = _stream.Finish();
        snapshot.Refs = _streamRefs.Refs.AsSpan(0, _streamRefs.RefCount).ToArray();
        snapshot.Bytes = snapshot.Chunks.Sum(c => (long)c.Length) + snapshot.Refs.LongLength * IntPtr.Size + _pendingStrings + 64 +
                         52L * (snapshot.Contacts?.Count ?? 0);
        _pendingSettleMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    // Waits for the pending snapshot to be settled and keeps it. With trim, the oldest go if it takes the snapshots past
    // the memory budget (not while restoring or truncating, which mustn't move the snapshots' indices).
    private void Keep(bool trim)
    {
        var settling = _settling!;
        _settling = null;
        settling.GetAwaiter().GetResult();

        var snapshot = _pending!;
        _pending = null;
        Bytes += snapshot.Bytes;
        (_base, _current) = (_current, _base);
        _baseSnapshot = snapshot;
        if (snapshot.Base is null)
            _wholeBytes = snapshot.Bytes;
        else
            _changedBytes = snapshot.Bytes;
        if (_pendingTimed)
            _averageSettle = Average(_averageSettle, _pendingSettleMs);
        Adapt();
        if (trim)
        {
            while (Bytes > MemoryBudget && _snapshots.Count > 1)
                DropOldest();
        }
    }

    // One record of the pending snapshot: unchanged or changed from the entity's record in the last snapshot (just the
    // bytes that changed, and its strings if they did), or whole.
    private void SettleEntry(int i)
    {
        var entry = _current.Entries[i];
        var bytes = _current.Buffer.Span(entry.Offset, entry.Length);
        var refs = _current.Buffer.Refs.AsSpan(entry.RefStart, entry.RefCount);
        var j = _baseSnapshot is null ? -1 : _base.Find(entry.Id, i + _shift);
        ReadOnlySpan<byte> before = default;
        ReadOnlySpan<string?> beforeRefs = default;
        if (j >= 0)
        {
            _shift = j - i;
            var was = _base.Entries[j];
            before = _base.Buffer.Span(was.Offset, was.Length);
            beforeRefs = _base.Buffer.Refs.AsSpan(was.RefStart, was.RefCount);
        }

        // Strings are counted where they're new (the same string as last time is already counted).
        var sameRefs = beforeRefs.Length == refs.Length;
        for (var r = 0; r < refs.Length; r++)
        {
            if (sameRefs && ReferenceEquals(refs[r], beforeRefs[r]))
                continue;
            if (refs[r] is { } text)
                _pendingStrings += 24 + 2L * text.Length;
            if (sameRefs && !string.Equals(refs[r], beforeRefs[r], StringComparison.Ordinal))
                sameRefs = false;
        }

        var wholeSize = 1 + VarSize(entry.Children) + VarSize(entry.Length) + entry.Length + VarSize(entry.RefCount);
        if (_pending!.Base is not null && j >= 0 && before.Length == bytes.Length && Diff(before, bytes) is var runs and >= 0)
        {
            var size = 1 + VarSize(entry.Children) + VarSize(ZigZag(j - i)) + VarSize(runs) + (sameRefs ? 0 : VarSize(entry.RefCount));
            for (int r = 0, last = 0; r < runs; r++)
            {
                var (from, to) = (_runs[2 * r], _runs[2 * r + 1]);
                size += VarSize(from - last) + VarSize(to - from) + (to - from);
                last = to;
            }

            if (size < wholeSize)
            {
                _stream.Begin(size);
                _stream.Byte((byte)(sameRefs ? Kind.Changed : Kind.ChangedStrings));
                _stream.Var(entry.Children);
                _stream.Var(ZigZag(j - i));
                _stream.Var(runs);
                for (int r = 0, last = 0; r < runs; r++)
                {
                    var (from, to) = (_runs[2 * r], _runs[2 * r + 1]);
                    _stream.Var(from - last);
                    _stream.Var(to - from);
                    _stream.Bytes(bytes[from..to]);
                    last = to;
                }

                if (!sameRefs)
                {
                    _stream.Var(entry.RefCount);
                    _streamRefs.AddRefs(refs);
                }

                return;
            }
        }

        _stream.Begin(wholeSize);
        _stream.Byte((byte)Kind.Whole);
        _stream.Var(entry.Children);
        _stream.Var(entry.Length);
        _stream.Bytes(bytes);
        _stream.Var(entry.RefCount);
        _streamRefs.AddRefs(refs);
    }

    // The runs of bytes that differ (into _runs, as from and to), or -1 if there are too many to be worth it. Equal
    // stretches are found with the runtime's vectorized comparison, so this stays quick in a Debug build too.
    private int Diff(ReadOnlySpan<byte> before, ReadOnlySpan<byte> now)
    {
        // A gap this long between two changed stretches is cheaper kept as two runs than as one.
        const int Gap = 3;
        var runs = 0;
        var at = 0;
        while (true)
        {
            at += before[at..].CommonPrefixLength(now[at..]);
            if (at >= now.Length)
                return runs;
            if (2 * runs == _runs.Length)
                return -1;

            var end = at + 1;
            while (end < now.Length)
            {
                var same = before[end..].CommonPrefixLength(now[end..]);
                if (same >= Gap || end + same >= now.Length)
                    break;
                end += same + 1;
            }

            _runs[2 * runs] = at;
            _runs[2 * runs + 1] = end;
            runs++;
            at = end;
        }
    }

    // The records of snapshot, decoded from the last whole one before it.
    private static Frame Decode(Snapshot snapshot)
    {
        var chain = new Stack<Snapshot>();
        for (var s = snapshot; s is not null; s = s.Base)
            chain.Push(s);

        Frame? before = null;
        while (chain.TryPop(out var s))
        {
            var frame = new Frame();
            DecodeInto(s, before, frame);
            before = frame;
        }

        return before!;
    }

    private static void DecodeInto(Snapshot snapshot, Frame? before, Frame frame)
    {
        var buffer = frame.Buffer;
        var chunk = 0;
        var at = 0;
        var refAt = 0;
        for (var i = 0; i < snapshot.Entities; i++)
        {
            if (at == snapshot.Used[chunk])
                (chunk, at) = (chunk + 1, 0);
            var data = snapshot.Chunks[chunk];
            var kind = (Kind)data[at++];
            var children = ReadVar(data, ref at);
            int offset, length, refStart = buffer.RefCount, refCount;
            if (kind == Kind.Whole)
            {
                length = ReadVar(data, ref at);
                offset = buffer.Reserve(length);
                data.AsSpan(at, length).CopyTo(buffer.Data.AsSpan(offset));
                at += length;
                refCount = ReadVar(data, ref at);
                buffer.AddRefs(snapshot.Refs.AsSpan(refAt, refCount));
                refAt += refCount;
            }
            else
            {
                var was = before!.Entries[i + UnZigZag(ReadVar(data, ref at))];
                length = was.Length;
                offset = buffer.Reserve(length);
                var target = buffer.Data.AsSpan(offset, length);
                before.Buffer.Span(was.Offset, length).CopyTo(target);
                var runs = ReadVar(data, ref at);
                for (int r = 0, position = 0; r < runs; r++)
                {
                    position += ReadVar(data, ref at);
                    var run = ReadVar(data, ref at);
                    data.AsSpan(at, run).CopyTo(target[position..]);
                    (at, position) = (at + run, position + run);
                }

                if (kind == Kind.Changed)
                {
                    refCount = was.RefCount;
                    buffer.AddRefs(before.Buffer.Refs.AsSpan(was.RefStart, refCount));
                }
                else
                {
                    refCount = ReadVar(data, ref at);
                    buffer.AddRefs(snapshot.Refs.AsSpan(refAt, refCount));
                    refAt += refCount;
                }
            }

            frame.Add(new Entry(SnapshotCodec.IdOf(buffer.Span(offset, length)), offset, length, refStart, refCount, children));
        }
    }

    private Entity Read(Frame frame, ref int next, SnapshotReader reader, HashSet<Guid> seen)
    {
        var entry = frame.Entries[next++];
        reader.Reset(frame.Buffer.Data, entry.Offset, frame.Buffer.Refs, entry.RefStart);
        var entity = _codec.Read(reader, seen);
        for (var i = 0; i < entry.Children; i++)
        {
            var child = Read(frame, ref next, reader, seen);
            entity.ChildList.Add(child);
            child.Parent = entity;
        }

        return entity;
    }

    // A snapshot has gone: its chunks and what physics remembered with it are used again.
    private void Return(Snapshot snapshot)
    {
        _stream.Return(snapshot);
        if (snapshot.Contacts is { } contacts)
        {
            _contactPool.Push(contacts);
            snapshot.Contacts = null;
        }
    }

    // The oldest goes. One that a kept snapshot is still written against stays in memory (and counted) until the oldest
    // kept one is whole.
    private void DropOldest()
    {
        var dropped = _snapshots[0];
        _snapshots.RemoveAt(0);
        if (dropped.Base is null)
            Release();
        _retained.Add(dropped);
        if (_snapshots.Count == 0 || _snapshots[0].Base is null)
            Release();
    }

    private void Release()
    {
        foreach (var snapshot in _retained)
        {
            Bytes -= snapshot.Bytes;
            // The one the next snapshot is compared with keeps its chunks while it's written against.
            if (snapshot != _baseSnapshot)
                Return(snapshot);
        }

        _retained.Clear();
    }

    // Spaces snapshots out for a big scene: so the capacity's worth fits the memory budget, and (with a cost per step)
    // so the time copying them out takes, spread over the steps between them, stays under it.
    private void Adapt()
    {
        // As many as fit in the budget (one in KeyEvery whole), less the dropped ones kept until a whole one is oldest,
        // reaching back as far as the capacity's worth would.
        var average = (_wholeBytes + (KeyEvery - 1) * (double)_changedBytes) / KeyEvery;
        var fit = MemoryBudget / average - (KeyEvery - 1);
        var forMemory = _framesPerSnapshot * _capacity / Math.Max(fit, 1);
        var forTime = CostPerStep > TimeSpan.Zero ? Math.Min(_averageCapture / CostPerStep.TotalMilliseconds, MostStepsForTime) : 0;
        Spacing = Math.Max(_framesPerSnapshot, (int)Math.Ceiling(Math.Max(forMemory, forTime)));
    }

    private static double Average(double average, double value) => average == 0 ? value : (average * 3 + value) / 4;

    private static int ZigZag(int value) => (value << 1) ^ (value >> 31);

    private static int UnZigZag(int value) => (int)((uint)value >> 1) ^ -(value & 1);

    private static int VarSize(int value) => value < 0x80 ? 1 : value < 0x4000 ? 2 : value < 0x200000 ? 3 : value < 0x10000000 ? 4 : 5;

    private static int ReadVar(byte[] data, ref int at)
    {
        var value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = data[at++];
            value |= (b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
    }

    // How a snapshot keeps an entity's record.
    private enum Kind : byte
    {
        // All of it.
        Whole,

        // The bytes that changed from its record in the snapshot before; its strings are the same.
        Changed,

        // The bytes that changed, and all its strings.
        ChangedStrings,
    }

    // A record in a frame: where its bytes and strings are, and how many children follow it (records are depth first).
    private readonly record struct Entry(Guid Id, int Offset, int Length, int RefStart, int RefCount, int Children);

    // Every entity's whole record, depth first: what copying a scene out gives, and decoding a snapshot.
    private sealed class Frame
    {
        private readonly Dictionary<Guid, int> _index = [];
        private bool _indexed;

        public SnapshotBuffer Buffer { get; } = new();

        public Entry[] Entries { get; private set; } = new Entry[64];

        public int Count { get; private set; }

        public void Clear()
        {
            Buffer.Clear();
            Count = 0;
            _indexed = false;
        }

        public void Add(in Entry entry)
        {
            if (Count == Entries.Length)
            {
                var entries = Entries;
                Array.Resize(ref entries, Count * 2);
                Entries = entries;
            }

            Entries[Count++] = entry;
        }

        // Where the entity's record is: usually at the guess (where it was, give or take what came or went before it).
        public int Find(Guid id, int guess)
        {
            if (guess >= 0 && guess < Count && Entries[guess].Id == id)
                return guess;
            if (!_indexed)
            {
                _index.Clear();
                for (var i = 0; i < Count; i++)
                    _index[Entries[i].Id] = i;
                _indexed = true;
            }

            return _index.TryGetValue(id, out var index) ? index : -1;
        }
    }

    // A snapshot: its records as the bytes that changed since Base (or whole, without one) in chunks, and their strings.
    private sealed class Snapshot(double time, long sequence, Snapshot? basis)
    {
        public double Time { get; } = time;

        public long Sequence { get; } = sequence;

        // The snapshot it's written against (the one before it), or null if it's whole.
        public Snapshot? Base { get; } = basis;

        // How many snapshots back the whole one it's decoded from is.
        public int Chain { get; } = basis is null ? 0 : basis.Chain + 1;

        public int Entities { get; set; }

        public byte[][] Chunks { get; set; } = [];

        // How much of each chunk is written.
        public int[] Used { get; set; } = [];

        public string?[] Refs { get; set; } = [];

        // What physics remembered of the scene then (how hard touching bodies that rotate pushed), or null for nothing.
        public ContactMemory? Contacts { get; set; }

        // What it adds to memory: its chunks, its strings, and the strings in it that are new.
        public long Bytes { get; set; }
    }

    // Writes a snapshot's records into chunks taken from a pool and given back when the snapshot goes, so a game that
    // plays on makes no garbage. A record is never split across two chunks.
    private sealed class ChunkWriter
    {
        // Chunks of snapshots that have gone, to use again (up to 64 MB of them).
        private readonly Stack<byte[]> _pool = new();
        private readonly List<byte[]> _chunks = [];
        private readonly List<int> _used = [];
        private byte[]? _chunk;
        private int _at;

        public void Reset()
        {
            _chunks.Clear();
            _used.Clear();
            _chunk = null;
            _at = 0;
        }

        public void Begin(int size)
        {
            if (_chunk is not null && _at + size <= _chunk.Length)
                return;
            if (_chunk is not null)
                _used.Add(_at);
            _chunk = size <= ChunkSize && _pool.TryPop(out var pooled) ? pooled : GC.AllocateUninitializedArray<byte>(Math.Max(size, ChunkSize));
            _chunks.Add(_chunk);
            _at = 0;
        }

        public void Byte(byte value) => _chunk![_at++] = value;

        public void Var(int value)
        {
            var v = (uint)value;
            while (v >= 0x80)
            {
                _chunk![_at++] = (byte)(v | 0x80);
                v >>= 7;
            }

            _chunk![_at++] = (byte)v;
        }

        public void Bytes(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(_chunk.AsSpan(_at));
            _at += bytes.Length;
        }

        // The chunks written, the last cut to what's in it (and the whole one given back).
        public (byte[][] Chunks, int[] Used) Finish()
        {
            if (_chunk is not null)
            {
                var last = _chunk.AsSpan(0, _at).ToArray();
                Give(_chunk);
                _chunks[^1] = last;
                _used.Add(_at);
            }

            var result = (_chunks.ToArray(), _used.ToArray());
            Reset();
            return result;
        }

        // Gives a snapshot's chunks back to the pool, once nothing reads them.
        public void Return(Snapshot snapshot)
        {
            foreach (var chunk in snapshot.Chunks)
                Give(chunk);
            snapshot.Chunks = [];
            snapshot.Used = [];
        }

        private void Give(byte[] chunk)
        {
            if (chunk.Length == ChunkSize && _pool.Count < 64 * 1024 * 1024 / ChunkSize)
                _pool.Push(chunk);
        }
    }
}
