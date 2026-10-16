using Griff.Audio;
using Griff.Scenes;

namespace Griff.Templates;

/// <summary>
/// The sounds and music the templates come with (all but Empty and BunnyMark), made by the Sound Maker's synths
/// (so they open in it to tweak), and hooked up to everything in the template that makes a noise.
/// </summary>
public static class TemplateSounds
{
    public const string Jump = "Sounds/jump.wav";
    public const string Coin = "Sounds/coin.wav";
    public const string Hurt = "Sounds/hurt.wav";
    public const string Hit = "Sounds/hit.wav";
    public const string Explode = "Sounds/explode.wav";
    public const string Shoot = "Sounds/shoot.wav";
    public const string Swing = "Sounds/swing.wav";
    public const string Dash = "Sounds/dash.wav";
    public const string PowerUp = "Sounds/powerup.wav";
    public const string Spring = "Sounds/spring.wav";
    public const string Select = "Sounds/select.wav";
    public const string Switch = "Sounds/switch.wav";
    public const string Dig = "Sounds/dig.wav";
    public const string Place = "Sounds/place.wav";
    public const string Music = "Music/theme.wav";

    // Each sound is the first of its kind (counting seeds up from its own) short enough to suit it.
    private static readonly (string Path, SoundPreset Preset, int Seed, float MaxSeconds, float Volume)[] Effects =
    [
        (Jump, SoundPreset.Jump, 3, 0.35f, 0.55f),
        (Coin, SoundPreset.Coin, 7, 0.45f, 0.5f),
        (Hurt, SoundPreset.Hit, 2, 0.3f, 0.6f),
        (Hit, SoundPreset.Hit, 11, 0.25f, 0.55f),
        (Explode, SoundPreset.Explosion, 5, 0.9f, 0.6f),
        (Shoot, SoundPreset.Shoot, 4, 0.25f, 0.35f),
        (Swing, SoundPreset.Shoot, 21, 0.25f, 0.45f),
        (Dash, SoundPreset.Shoot, 30, 0.3f, 0.4f),
        (PowerUp, SoundPreset.PowerUp, 6, 0.7f, 0.5f),
        (Spring, SoundPreset.Jump, 14, 0.45f, 0.55f),
        (Select, SoundPreset.Blip, 1, 0.15f, 0.45f),
        (Switch, SoundPreset.Blip, 9, 0.2f, 0.5f),
        (Dig, SoundPreset.Hit, 40, 0.15f, 0.3f),
        (Place, SoundPreset.Blip, 13, 0.15f, 0.4f),
    ];

    /// <summary>
    /// The sound effects every template with sounds comes with, plus <paramref name="music"/>, a template's own sounds
    /// (<paramref name="more"/>) and its own music (<paramref name="moreMusic"/>), keyed by path relative to Assets, as WAV
    /// bytes.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Files(MusicRecipe music, IReadOnlyDictionary<string, SoundRecipe>? more = null,
        IReadOnlyDictionary<string, MusicRecipe>? moreMusic = null)
    {
        var files = new Dictionary<string, byte[]>();
        foreach (var (path, recipe) in moreMusic ?? new Dictionary<string, MusicRecipe>())
            files[path] = Wav.Encode(MusicSynth.Render(recipe), music: recipe);
        foreach (var (path, recipe) in more ?? new Dictionary<string, SoundRecipe>())
            files[path] = Wav.Encode(SoundSynth.Render(recipe), recipe);
        foreach (var (path, preset, seed, maxSeconds, volume) in Effects)
        {
            var recipe = Pick(preset, seed, maxSeconds) with { Volume = volume };
            files[path] = Wav.Encode(SoundSynth.Render(recipe), recipe);
        }

        files[Music] = Wav.Encode(MusicSynth.Render(music), music: music);
        return files;
    }

    private static SoundRecipe Pick(SoundPreset preset, int seed, float maxSeconds)
    {
        for (var s = seed; ; s++)
        {
            var recipe = SoundRecipe.Preset(preset, new Random(s));
            if (SoundSynth.Render(recipe).Seconds <= maxSeconds || s > seed + 200)
                return recipe;
        }
    }

    /// <summary>Gives a scene its music: an entity with <see cref="BackgroundMusic"/>.</summary>
    public static void AddMusic(Scene scene)
    {
        var entity = new Entity("Music");
        var music = entity.AddComponent<BackgroundMusic>();
        music.Music = Music;
        music.Volume = 0.5f;
        scene.Add(entity);
    }

    /// <summary>Sets a sound with <paramref name="set"/> to <paramref name="sound"/> if it has none yet (<paramref name="current"/> is empty).</summary>
    public static void Set(string? current, Action<string> set, string sound)
    {
        if (string.IsNullOrWhiteSpace(current))
            set(sound);
    }

    /// <summary>
    /// Fills in the sound of everything of Griff's core in <paramref name="entity"/> that makes one and has none yet. A
    /// module's template fills in its own components' too (see <see cref="ProjectTemplate.WireSounds"/>).
    /// </summary>
    public static void Wire(Entity entity)
    {
        var isPlayer = entity.GetComponent<Collider>()?.Layer == "Player";
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case PlatformerMovement m:
                    Set(m.JumpSound, s => m.JumpSound = s, Jump);
                    Set(m.DashSound, s => m.DashSound = s, Dash);
                    break;
                case TopDownMovement m:
                    Set(m.DashSound, s => m.DashSound = s, Dash);
                    break;
                case Pickup p:
                    Set(p.Sound, s => p.Sound = s, string.IsNullOrWhiteSpace(p.Item) ? Coin : PowerUp);
                    break;
                case Health h when isPlayer:
                    Set(h.HurtSound, s => h.HurtSound = s, Hurt);
                    break;
                case Health h:
                    Set(h.HurtSound, s => h.HurtSound = s, Hit);
                    Set(h.DeathSound, s => h.DeathSound = s, Explode);
                    break;
                case Attack a:
                    Set(a.Sound, s => a.Sound = s, Swing);
                    break;
                case BumpAttack b:
                    Set(b.Sound, s => b.Sound = s, Swing);
                    break;
                case Inventory i when i.ItemUses.Count > 0:
                    Set(i.UseSound, s => i.UseSound = s, PowerUp);
                    break;
                case Shooter s:
                    Set(s.Sound, v => s.Sound = v, Shoot);
                    break;
                case Checkpoint c:
                    Set(c.Sound, s => c.Sound = s, PowerUp);
                    break;
                case Door d:
                    Set(d.Sound, s => d.Sound = s, PowerUp);
                    break;
                case Scenes.Switch w:
                    Set(w.Sound, s => w.Sound = s, Switch);
                    break;
                case Bouncer b:
                    Set(b.Sound, s => b.Sound = s, Spring);
                    break;
                case Button b:
                    Set(b.Sound, s => b.Sound = s, Select);
                    break;
                case Slider sl:
                    Set(sl.Sound, s => sl.Sound = s, Select);
                    break;
                case Choice ch:
                    Set(ch.Sound, s => ch.Sound = s, Select);
                    break;
            }
        }
    }
}
