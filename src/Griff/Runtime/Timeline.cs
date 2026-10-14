using System.Diagnostics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Rewind for play mode: keeps periodic snapshots of the running scene so you can step back in time, inspect or tweak,
/// and resume from there. A snapshot holds what a scene file does (public script properties included; private script
/// fields restart from their defaults) and restores as the file would load, but is kept in a compact binary form made
/// without garbage, and an entity that hasn't changed since the last snapshot shares its record with it. Big scenes are
/// snapshotted less often, so the snapshots stay within <see cref="MemoryBudget"/> and still reach as far back.
/// </summary>
public sealed class Timeline
{
    // A record of one entity: where its bytes are (its own snapshot's buffer, or an earlier one's it shares), how many
    // children follow it (records are depth first), and the snapshot that wrote the bytes.
    private readonly record struct EntityRecord(byte[] Buffer, int Offset, int Length, int Children, long Origin);

    private sealed class Snapshot(double time, long sequence, EntityRecord[] records, long bytes)
    {
        public double Time { get; } = time;

        public long Sequence { get; } = sequence;

        public EntityRecord[] Records { get; } = records;

        // What it added to memory: the bytes of the records it wrote and its list of records.
        public long Bytes { get; } = bytes;
    }

    private static readonly int RecordSize = System.Runtime.CompilerServices.Unsafe.SizeOf<EntityRecord>();

    private readonly SnapshotCodec _codec;
    private readonly int _capacity;
    private readonly int _framesPerSnapshot;
    private readonly List<Snapshot> _snapshots = [];
    private readonly SnapshotBuffer _scratch = new();
    private readonly List<EntityRecord> _records = [];
    private readonly Dictionary<Guid, int> _previousIds = [];
    private Snapshot? _indexed;
    private long _sequence;
    private int _stepsSinceSnapshot;
    private double _averageBytes;
    private double _averageMilliseconds;

    /// <param name="capacity">The most snapshots kept: past it, the oldest go.</param>
    /// <param name="framesPerSnapshot">Steps between snapshots (6, ten a second at 60 steps a second) while they're small.</param>
    public Timeline(SceneSerializer serializer, int capacity = 200, int framesPerSnapshot = 6)
    {
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
    /// The most time a step may spend on snapshots, on average (none unless set): a scene that takes longer to snapshot is
    /// snapshotted less often, though at least every <see cref="MostStepsForTime"/> steps. The editor sets it so a big
    /// scene keeps its frame rate while it plays: a snapshot is taken between two steps, so one that takes long makes a
    /// frame miss the screen's beat, and few enough of them don't show in its 1% low.
    /// </summary>
    public TimeSpan CostPerStep { get; init; }

    /// <summary>The most steps <see cref="CostPerStep"/> spaces snapshots out to: 120, two seconds at 60 steps a second.</summary>
    public const int MostStepsForTime = 120;

    public int Count => _snapshots.Count;

    /// <summary>Game time of each snapshot, oldest first.</summary>
    public IEnumerable<double> Times => _snapshots.Select(s => s.Time);

    public double TimeAt(int index) => _snapshots[index].Time;

    /// <summary>Steps between snapshots now: the steps it was made with, or more for a big scene (see <see cref="MemoryBudget"/>).</summary>
    public int Spacing { get; private set; }

    /// <summary>Memory the kept snapshots take, in bytes.</summary>
    public long Bytes { get; private set; }

    /// <summary>Call once per step of the game. Takes a snapshot every <see cref="Spacing"/> steps and drops the oldest past capacity.</summary>
    public void Record(Scene scene, double time, bool force = false)
    {
        if (!force && ++_stepsSinceSnapshot < Spacing)
            return;

        _stepsSinceSnapshot = 0;
        var start = Stopwatch.GetTimestamp();
        var snapshot = Capture(scene, time);
        _snapshots.Add(snapshot);
        Bytes += snapshot.Bytes;
        while (_snapshots.Count > _capacity || (Bytes > MemoryBudget && _snapshots.Count > 1))
            DropOldest();
        // The first takes longer, with the code that writes snapshots made ready as it goes.
        Adapt(snapshot.Bytes, force ? null : Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>A fresh scene in the state of snapshot <paramref name="index"/>.</summary>
    public Scene Restore(int index)
    {
        var records = _snapshots[index].Records;
        var reader = new SnapshotReader();
        var seen = new HashSet<Guid>();
        var scene = new Scene();
        var next = 0;
        while (next < records.Length)
            scene.Add(Read(records, ref next, reader, seen));
        return scene;
    }

    /// <summary>Forgets everything after <paramref name="index"/>, so resuming from it starts a new future.</summary>
    public void TruncateAfter(int index)
    {
        while (_snapshots.Count > index + 1)
        {
            Bytes -= _snapshots[^1].Bytes;
            _snapshots.RemoveAt(_snapshots.Count - 1);
        }

        _stepsSinceSnapshot = 0;
    }

    public void Clear()
    {
        _snapshots.Clear();
        _previousIds.Clear();
        _indexed = null;
        Bytes = 0;
        _stepsSinceSnapshot = 0;
    }

    // Writes each entity's record, depth first; one whose bytes are the same as in the last snapshot shares its record,
    // as long as the snapshot that wrote it is still kept (so a long-unchanged entity can't hold on to old buffers).
    private Snapshot Capture(Scene scene, double time)
    {
        var sequence = ++_sequence;
        var previous = _snapshots.Count > 0 ? _snapshots[^1] : null;
        var oldest = _snapshots.Count > 0 ? _snapshots[0].Sequence : sequence;
        _scratch.Truncate(0);
        _records.Clear();
        var roots = scene.Roots;
        for (var i = 0; i < roots.Count; i++)
            Write(roots[i], previous, oldest, sequence);

        var own = _scratch.ToArray();
        var records = new EntityRecord[_records.Count];
        for (var i = 0; i < records.Length; i++)
        {
            var record = _records[i];
            records[i] = record.Origin == sequence ? record with { Buffer = own } : record;
        }

        return new Snapshot(time, sequence, records, own.LongLength + (long)records.Length * RecordSize);
    }

    private void Write(Entity entity, Snapshot? previous, long oldest, long sequence)
    {
        var offset = _scratch.Length;
        _codec.Write(entity, _scratch);
        var length = _scratch.Length - offset;
        var children = entity.Children;
        var record = new EntityRecord(null!, offset, length, children.Count, sequence);
        if (previous is not null && Find(previous, entity.Id, _records.Count) is { } before && before.Origin >= oldest &&
            before.Length == length && before.Buffer.AsSpan(before.Offset, length).SequenceEqual(_scratch.Span(offset, length)))
        {
            // Unchanged: the bytes just written go again, and the record is the last snapshot's.
            _scratch.Truncate(offset);
            record = before with { Children = children.Count };
        }

        _records.Add(record);
        for (var i = 0; i < children.Count; i++)
            Write(children[i], previous, oldest, sequence);
    }

    // The last snapshot's record of the entity: usually at the same place, as long as nothing came or went before it.
    private EntityRecord? Find(Snapshot previous, Guid id, int at)
    {
        var records = previous.Records;
        if (at < records.Length && SnapshotCodec.IdOf(records[at].Buffer.AsSpan(records[at].Offset)) == id)
            return records[at];

        if (_indexed != previous)
        {
            _previousIds.Clear();
            for (var i = 0; i < records.Length; i++)
                _previousIds[SnapshotCodec.IdOf(records[i].Buffer.AsSpan(records[i].Offset))] = i;
            _indexed = previous;
        }

        return _previousIds.TryGetValue(id, out var index) ? records[index] : null;
    }

    private Entity Read(EntityRecord[] records, ref int next, SnapshotReader reader, HashSet<Guid> seen)
    {
        var record = records[next++];
        reader.Reset(record.Buffer, record.Offset);
        var entity = _codec.Read(reader, seen);
        for (var i = 0; i < record.Children; i++)
        {
            var child = Read(records, ref next, reader, seen);
            entity.ChildList.Add(child);
            child.Parent = entity;
        }

        return entity;
    }

    private void DropOldest()
    {
        Bytes -= _snapshots[0].Bytes;
        _snapshots.RemoveAt(0);
    }

    // Spaces snapshots out for a big scene: so the capacity's worth fits the memory budget, and (with a cost per step)
    // so their time spread over the steps between them stays under it.
    private void Adapt(long bytes, double? milliseconds)
    {
        _averageBytes = _averageBytes == 0 ? bytes : (_averageBytes * 3 + bytes) / 4;
        if (milliseconds is { } taken)
            _averageMilliseconds = _averageMilliseconds == 0 ? taken : (_averageMilliseconds * 3 + taken) / 4;
        var forMemory = _framesPerSnapshot * _capacity * _averageBytes / MemoryBudget;
        var forTime = CostPerStep > TimeSpan.Zero ? Math.Min(_averageMilliseconds / CostPerStep.TotalMilliseconds, MostStepsForTime) : 0;
        Spacing = Math.Max(_framesPerSnapshot, (int)Math.Ceiling(Math.Max(forMemory, forTime)));
    }
}
