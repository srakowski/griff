using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Atmosphere: lights in the dark, and rooms the camera moves between.
public sealed partial class Game
{
    // The music rooms last started, so walking back into a room with the same music doesn't start it over.
    private string? _roomMusic;

    /// <summary>
    /// How lit <paramref name="point"/> is, from 0 (pitch dark) to 1 (fully lit): the camera's
    /// <see cref="Camera.Ambient"/> light plus every <see cref="Light"/> reaching it, as the game is drawn. In a scene
    /// that isn't dark it's always 1. For stealth: <c>if (Game.LightAt(player) &lt; 0.3f) return; // can't be seen</c>
    /// </summary>
    public float LightAt(Vector2 point)
    {
        var camera = SceneRenderer.FindCamera(Scene)?.GetComponent<Camera>();
        if (camera is not { IsDark: true })
            return 1;
        var frame = new RenderFrame
        {
            Background = Color.Black,
            ViewProjection = Matrix4x4.Identity,
            Sprites = [],
            Ambient = camera.CurrentAmbient,
            Lights = SceneRenderer.LightDraws(Scene).ToList(),
        };
        var light = frame.LightAt(point);
        return Math.Max(light.R, Math.Max(light.G, light.B));
    }

    /// <summary>How lit <paramref name="entity"/> is where it stands; see <see cref="LightAt(Vector2)"/>.</summary>
    public float LightAt(Entity entity) =>
        LightAt(new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y));

    // Before followers move: a follower's target that has crossed into another room takes the camera over to it.
    private void RunRooms()
    {
        var rooms = Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Room>()).ToList();
        foreach (var follow in Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Follow>()).ToList())
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
            var room = rooms.FirstOrDefault(r => r.Area.Contains(position));
            if (room is null || room == follow.CurrentRoom)
                continue;

            // The room it starts in is entered too, but there's nothing to slide over from.
            var first = follow.CurrentRoom?.Entity?.Scene != Scene;
            if (follow.CurrentRoom is { } old)
                old.IsCurrent = false;
            follow.CurrentRoom = room;
            room.IsCurrent = true;
            var seconds = first ? 0 : Math.Max(0, room.Transition);
            follow.SlideOver(seconds);
            if (room.Freeze && seconds > 0)
                Time.Freeze(seconds);
            camera.FadeAmbient(room.Ambient.A > 0 ? room.Ambient with { A = 1 } : null, first ? 0 : Math.Max(seconds, 0.25f));

            if (!string.IsNullOrWhiteSpace(room.Music) && room.Music.Trim() != _roomMusic)
            {
                _roomMusic = room.Music.Trim();
                Audio.CrossfadeMusic(_roomMusic, first ? 0 : Math.Max(0.5f, seconds * 2));
            }

            room.RaiseEntered(target);
            if (!string.IsNullOrWhiteSpace(room.Message))
                Send(room.Message.Trim(), room.Entity);
        }
    }

    /// <summary>
    /// Plays a sound as if it came from <paramref name="position"/>: from the left or right speaker as it's to the left or
    /// right of the middle of the screen, and quieter the further it is off screen, out to a screen's width beyond the
    /// edge (where it can't be heard at all).
    /// </summary>
    public void PlaySoundAt(string sound, Vector2 position, float volume = 1, float pitch = 1) =>
        PlaySoundAt(sound, position, volume, pitch, relay: true);

    // On the host of a network game, everyone hears it, from where it is on their own screen.
    internal void PlaySoundAt(string sound, Vector2 position, float volume, float pitch, bool relay)
    {
        if (relay)
            Net.RelaySound(sound, volume, pitch, position);
        var (loudness, pan) = Hearing(position, range: 0);
        if (loudness <= 0)
            return;
        if (pitch == 1)
            Audio.Play(sound, volume * loudness, pan);
        else
            Audio.Play(sound, volume * loudness, pan, pitch);
    }

    // Sound players that have just appeared play their sound if they're set to; scenes' music starts.
    private void RunSoundPlayers()
    {
        foreach (var player in Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<SoundPlayer>()))
        {
            if (player.Started)
                continue;
            player.Started = true;
            if (player.OnStart)
                Play(player);
        }

        foreach (var music in Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<BackgroundMusic>()))
        {
            if (_startedMusic.Contains(music) || string.IsNullOrWhiteSpace(music.Music))
                continue;
            _startedMusic.Add(music);
            _roomMusic = null;
            Audio.CrossfadeMusic(music.Music.Trim(), Math.Max(0, music.Fade), Math.Clamp(music.Volume, 0, 1), music.Loop);
        }
    }

    private readonly HashSet<BackgroundMusic> _startedMusic = [];

    // One play of a sound player, at its pitch give or take its variation.
    private void Play(SoundPlayer player)
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
            Audio.Play(sound, volume);
        else
            Audio.Play(sound, volume, 0, pitch);
    }

    // How loud something at a position sounds (0 to 1) and which side it's on (-1 to 1), heard from the middle of the
    // view. With a range, it fades out to nothing that far away; without, it's full on screen and fades over a screen's width.
    private (float Loudness, float Pan) Hearing(Vector2 position, float range)
    {
        if (FirstPersonHearing(position, range) is { } heard)
            return heard;
        var camera = SceneRenderer.FindCamera(Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
        var half = new Vector2(size * (ViewAspect > 0 ? ViewAspect : 1), size);
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

    private readonly Dictionary<SoundSource, ISoundLoop> _loops = [];

    // Each playing source keeps a loop going, as loud as it is near the middle of the view.
    private void RunSoundSources()
    {
        var live = new HashSet<SoundSource>();
        foreach (var source in Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<SoundSource>()))
        {
            if (!source.Playing || string.IsNullOrWhiteSpace(source.Sound))
                continue;
            live.Add(source);
            var center = source.Entity!.Transform.WorldPosition;
            var (loudness, pan) = source.Range > 0 ? Hearing(new Vector2(center.X, center.Y), source.Range) : (1, 0);
            if (!_loops.TryGetValue(source, out var loop) || !loop.IsPlaying || source.PlayingSound != source.Sound.Trim())
            {
                loop?.Stop();
                loop = Audio.Loop(source.Sound.Trim(), 0);
                source.PlayingSound = source.Sound.Trim();
                _loops[source] = loop;
            }

            loop.Volume = Math.Clamp(source.Volume, 0, 1) * loudness;
            loop.Pan = source.Range > 0 ? pan : 0;
            loop.Pitch = Math.Clamp(source.Pitch, 0.05f, 8);
            source.Loudness = loop.Volume;
        }

        foreach (var (source, loop) in _loops.Where(l => !live.Contains(l.Key)).ToList())
        {
            loop.Stop();
            source.Loudness = 0;
            _loops.Remove(source);
        }
    }

    private void StopSoundSources()
    {
        foreach (var (source, loop) in _loops)
        {
            loop.Stop();
            source.Loudness = 0;
        }

        _loops.Clear();
    }

    private void RunLights()
    {
        foreach (var light in Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Light>()))
            light.AdvanceFlicker(Time.Delta);
    }
}
