using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What's around the player: rooms (their music, and the camera taken over to the next), lights, sounds heard from
/// where they're played, sound sources and sound players, and the sound's own clock each step.
/// </summary>
internal sealed class AtmosphereSystem : GameSystem, ISounds
{
    // A sound player's Play action.
    void ISounds.Play(SoundPlayer player) => Play(player);

    // The music rooms last started, so walking back into a room with the same music doesn't start it over.
    private string? _roomMusic;

    private readonly HashSet<BackgroundMusic> _startedMusic = [];

    [NotState("the sounds playing: sound isn't rewound, so a rewind stops them and their sources start them again")]
    private readonly Dictionary<SoundSource, ISoundLoop> _loops = [];

    [NotState("the sources heard this step, gathered again each step")]
    private readonly HashSet<SoundSource> _liveSources = [];

    public override string Name => "Atmosphere";

    public float LightAt(Vector2 point)
    {
        var camera = Views.FindCamera(Game.Scene)?.GetComponent<Camera>();
        if (camera is not { IsDark: true })
            return 1;
        // As the frame works it out: lights count only while the ambient light is less than full.
        var ambient = camera.CurrentAmbient;
        var light = ambient.R < 1 || ambient.G < 1 || ambient.B < 1 ? LightDraw.Total(ambient, Views.LightDraws(Game.Scene).ToList(), point) : Color.White;
        return Math.Max(light.R, Math.Max(light.G, light.B));
    }

    public float LightAt(Entity entity) =>
        LightAt(new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y));

    // Before followers move: a follower's target that has crossed into another room takes the camera over to it.
    internal void RunRooms()
    {
        var rooms = Game.Scene.ActiveNow<Room>();
        foreach (var follow in Game.Scene.ActiveNow<Follow>())
        {
            var camera = follow.Entity!.GetComponent<Camera>();
            if (camera is null)
                continue;
            if (rooms.Count == 0)
            {
                follow.CurrentRoom = null;
                continue;
            }

            if (follow.FindTarget() is not { } target)
                continue;
            var position = new Vector2(target.Transform.WorldPosition.X, target.Transform.WorldPosition.Y);
            // A loop rather than a query that keeps `position`, which would cost the camera a little garbage every step.
            Room? room = null;
            foreach (var r in rooms)
            {
                if (r.Area.Contains(position))
                {
                    room = r;
                    break;
                }
            }

            if (room is null || room == follow.CurrentRoom)
                continue;

            // The room it starts in is entered too, but there's nothing to slide over from.
            var first = follow.CurrentRoom?.Entity?.Scene != Game.Scene;
            if (follow.CurrentRoom is { } old)
                old.IsCurrent = false;
            follow.CurrentRoom = room;
            room.IsCurrent = true;
            var seconds = first ? 0 : Math.Max(0, room.Transition);
            follow.SlideOver(seconds);
            if (room.Freeze && seconds > 0)
                Game.Time.Freeze(seconds);
            camera.FadeAmbient(room.Ambient.A > 0 ? room.Ambient with { A = 1 } : null, first ? 0 : Math.Max(seconds, 0.25f));

            if (!string.IsNullOrWhiteSpace(room.Music) && room.Music.Trim() != _roomMusic)
            {
                _roomMusic = room.Music.Trim();
                Game.Audio.CrossfadeMusic(_roomMusic, first ? 0 : Math.Max(0.5f, seconds * 2));
            }

            room.RaiseEntered(target);
            if (!string.IsNullOrWhiteSpace(room.Message))
                Game.Send(room.Message.Trim(), room.Entity, from: room.Entity);
        }
    }

    public void PlaySoundAt(string sound, Vector2 position, float volume = 1, float pitch = 1) =>
        PlaySoundAt(sound, position, volume, pitch, relay: true);

    // On the host of a network game, everyone hears it, from where it is on their own screen.
    internal void PlaySoundAt(string sound, Vector2 position, float volume, float pitch, bool relay)
    {
        if (relay)
            Game.Net.RelaySound(sound, volume, pitch, position);
        var (loudness, pan) = Hearing(position, range: 0);
        if (loudness <= 0)
            return;
        if (pitch == 1)
            Game.Audio.Play(sound, volume * loudness, pan);
        else
            Game.Audio.Play(sound, volume * loudness, pan, pitch);
    }

    // Sound players that have just appeared play their sound if they're set to; scenes' music starts.
    internal void RunSoundPlayers()
    {
        foreach (var player in Game.Scene.ActiveNow<SoundPlayer>())
        {
            if (player.Started)
                continue;
            player.Started = true;
            if (player.OnStart)
                Play(player);
        }

        foreach (var music in Game.Scene.ActiveNow<BackgroundMusic>())
        {
            if (_startedMusic.Contains(music) || string.IsNullOrWhiteSpace(music.Music))
                continue;
            _startedMusic.Add(music);
            _roomMusic = null;
            Game.Audio.CrossfadeMusic(music.Music.Trim(), Math.Max(0, music.Fade), Math.Clamp(music.Volume, 0, 1), music.Loop);
        }
    }

    // One play of a sound player, at its pitch give or take its variation.
    internal void Play(SoundPlayer player)
    {
        if (string.IsNullOrWhiteSpace(player.Sound))
            return;
        var sound = player.Sound.Trim();
        var pitch = Math.Max(0.05f, player.Pitch + (Random.Shared.NextSingle() * 2 - 1) * Math.Max(0, player.PitchVariation));
        var volume = Math.Clamp(player.Volume, 0, 1);
        if (player.Positional)
        {
            // Every machine has its own copy of a sound player, so each plays it itself.
            var at = player.Entity!.Transform.WorldPosition;
            PlaySoundAt(sound, new Vector2(at.X, at.Y), volume, pitch, relay: false);
        }
        else if (pitch == 1)
            Game.Audio.Play(sound, volume);
        else
            Game.Audio.Play(sound, volume, 0, pitch);
    }

    // Where a sound is heard from when the game is seen through someone's eyes (first person): their ears, turned the way
    // they face.
    private (float Loudness, float Pan)? EyesHearing(Vector2 position, float range)
    {
        if (Game.Eyes is not { } viewer)
            return null;
        var at = viewer.Transform.WorldPosition;
        var offset = position - new Vector2(at.X, at.Y);
        var distance = offset.Length();
        var angle = viewer.Transform.Rotation.Z * MathF.PI / 180;
        var right = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        var pan = distance > 1e-3f ? Vector2.Dot(offset / distance, right) * 0.7f : 0;
        var reach = range > 0 ? range : 24;
        var t = Math.Clamp(distance / reach, 0, 1);
        return ((1 - t) * (1 - t), pan);
    }

    // How loud something at a position sounds (0 to 1) and which side it's on (-1 to 1), heard from the middle of the
    // view. With a range, it fades out to nothing that far away; without, it's full on screen and fades over a screen's width.
    private (float Loudness, float Pan) Hearing(Vector2 position, float range)
    {
        if (EyesHearing(position, range) is { } heard)
            return heard;
        var camera = Views.FindCamera(Game.Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var half = new Vector2(size * (Game.ViewAspect > 0 ? Game.ViewAspect : 1), size);
        var listener = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        var offset = position - listener;
        var pan = Math.Clamp(offset.X / half.X, -1, 1) * 0.8f;

        float loudness;
        if (range > 0)
        {
            var t = Math.Clamp(offset.Length() / range, 0, 1);
            loudness = (1 - t) * (1 - t);
        }
        else
        {
            // How far outside the view, as a fraction of the view's size.
            var outside = Vector2.Max(Vector2.Abs(offset) - half, Vector2.Zero) / (half * 2);
            loudness = 1 - Math.Clamp(Math.Max(outside.X, outside.Y), 0, 1);
        }

        return (loudness, pan);
    }

    // Each playing source keeps a loop going, as loud as it is near the middle of the view.
    internal void RunSoundSources()
    {
        var live = _liveSources;
        live.Clear();
        foreach (var source in Game.Scene.ActiveNow<SoundSource>())
        {
            if (!source.Playing || string.IsNullOrWhiteSpace(source.Sound))
                continue;
            live.Add(source);
            var center = source.Entity!.Transform.WorldPosition;
            var (loudness, pan) = source.Range > 0 ? Hearing(new Vector2(center.X, center.Y), source.Range) : (1, 0);
            if (!_loops.TryGetValue(source, out var loop) || !loop.IsPlaying || source.PlayingSound != source.Sound.Trim())
            {
                loop?.Stop();
                loop = Game.Audio.Loop(source.Sound.Trim(), 0);
                source.PlayingSound = source.Sound.Trim();
                _loops[source] = loop;
            }

            loop.Volume = Math.Clamp(source.Volume, 0, 1) * loudness;
            loop.Pan = source.Range > 0 ? pan : 0;
            loop.Pitch = Math.Clamp(source.Pitch, 0.05f, 8);
            source.Loudness = loop.Volume;
        }

        if (_loops.Count == live.Count)
            return;
        foreach (var (source, loop) in _loops.Where(l => !_liveSources.Contains(l.Key)).ToList())
        {
            loop.Stop();
            source.Loudness = 0;
            _loops.Remove(source);
        }
    }

    // The sounds played for the entities before a rewind stop: those put back start theirs again.
    protected override void Restored() => StopSoundSources();

    internal void StopSoundSources()
    {
        foreach (var (source, loop) in _loops)
        {
            loop.Stop();
            source.Loudness = 0;
        }

        _loops.Clear();
    }

    internal void RunLights()
    {
        foreach (var light in Game.Scene.RunningNow<Light>())
            light.AdvanceFlicker(Game.Time.Delta);
    }

    internal void AdvanceAudio() => Game.Audio.Advance(Game.Time.UnscaledDelta);
}
