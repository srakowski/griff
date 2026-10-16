using Griff.Projects;
using Griff.Scenes;

namespace Griff.Editing.Presets;

/// <summary>
/// Creates music for the scene: an entity with a <see cref="BackgroundMusic"/>, which plays its Music (make one with Audio &gt;
/// New Music) when the scene starts, crossfading from whatever was playing.
/// </summary>
[Category("Audio")]
public sealed class MusicPreset : EntityPreset
{
    public override string Id => "Entity.CreateMusic";
    public override string Text => "Create _Music";
    public override int Order => 0;
    public override string? Icon => "Music";
    public override string EntityName(Scene scene) => "Music";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<BackgroundMusic>();
}

/// <summary>
/// Creates a sound that loops where it's put (a waterfall, a crackling fire): an entity with a <see cref="SoundSource"/>,
/// louder the nearer it is to the middle of the screen. Pick its Sound in the Inspector.
/// </summary>
[Category("Audio")]
public sealed class SoundSourcePreset : EntityPreset
{
    public override string Id => "Entity.CreateSoundSource";
    public override string Text => "Create Sound _Source";
    public override int Order => 1;
    public override string? Icon => "Speaker";
    public override string EntityName(Scene scene) => "Sound Source";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<SoundSource>();
}

/// <summary>
/// Creates a sound that plays when something happens, with no code: an entity with a <see cref="SoundPlayer"/>, which plays
/// its Sound whenever its Message is sent (by a pickup, a switch or a script).
/// </summary>
[Category("Audio")]
public sealed class SoundPlayerPreset : EntityPreset
{
    public override string Id => "Entity.CreateSoundPlayer";
    public override string Text => "Create Sound _Player";
    public override int Order => 2;
    public override string? Icon => "Volume";
    public override string EntityName(Scene scene) => "Sound Player";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<SoundPlayer>();
}

/// <summary>
/// Creates a beat clock that keeps time with the music: an entity with a <see cref="BeatClock"/>, which sends "Beat" on
/// every beat and "Bar" on every bar for sound players, spawners, movers and scripts to follow.
/// </summary>
[Category("Audio")]
public sealed class BeatClockPreset : EntityPreset
{
    public override string Id => "Entity.CreateBeatClock";
    public override string Text => "Create _Beat Clock";
    public override int Order => 3;
    public override string? Icon => "Stopwatch";
    public override string EntityName(Scene scene) => "Beat Clock";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<BeatClock>();
}

/// <summary>
/// Creates a calibration screen: a panel, closed until it's opened, with a <see cref="Calibration"/> that plays clicks for
/// the player to tap along to and offers to set their audio offset (how late they hear the sound, for Bluetooth headphones
/// and the like) to how late their taps came, its prompt, a slider to fine-tune the offset by hand, and Apply, Again and
/// Back buttons. Give an options menu a button whose Action is Open Panel and Panel is Calibrate.
/// </summary>
[Category("Audio")]
public sealed class CalibrationPreset : EntityPreset
{
    public override string Id => "Entity.CreateCalibration";
    public override string Text => "Create _Calibration Screen";
    public override int Order => 4;
    public override string? Icon => "Headphones";
    public override string EntityName(Scene scene) => CalibrationScreen.Name;
    public override void Configure(Entity entity, Scene scene) => CalibrationScreen.Configure(entity);
    public override IEnumerable<Entity> CreateChildren(Scene scene) => CalibrationScreen.Children();
}
