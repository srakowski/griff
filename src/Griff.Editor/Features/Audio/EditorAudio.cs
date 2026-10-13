using System.IO;
using System.Windows;
using Griff.Audio;
using Griff.Editor.Core.Workspace;
using Griff.Platform.Windows;

namespace Griff.Editor.Features.Audio;

/// <summary>
/// Sound in the editor: previews of sound files and freshly made sounds (one at a time), the project's sounds for
/// pickers, and the Sound Maker. Previews play through the same output as the game.
/// </summary>
public sealed class EditorAudio
{
    private readonly IWorkspace _workspace;
    private Voice? _preview;
    private SoundMakerWindow? _maker;

    public EditorAudio(IWorkspace workspace)
    {
        _workspace = workspace;
    }

    public static readonly string[] Extensions = [".wav", ".mp3", ".ogg"];

    /// <summary>The sound or music file being previewed, by asset path, while it plays.</summary>
    public string? Previewing { get; private set; }

    public bool IsPreviewing => _preview?.IsPlaying == true;

    /// <summary>Raised when a preview starts or stops being asked for.</summary>
    public event EventHandler? PreviewChanged;

    public string? AssetsPath => _workspace.Project?.AssetsPath;

    /// <summary>Every sound in the project, as asset paths ("Sounds/jump.wav"), sounds before music.</summary>
    public IReadOnlyList<string> Sounds()
    {
        if (AssetsPath is not { } root || !Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => !p.StartsWith('.') && !p.StartsWith("Build/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.StartsWith("Music/", StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The full path of an asset path, or null when there's no project or no such file.</summary>
    public string? Resolve(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || AssetsPath is not { } root)
            return null;
        var path = Path.Combine(root, assetPath.Trim().Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    /// <summary>Plays a sound file from the project, stopping any other preview. False if it can't be played.</summary>
    public bool Preview(string? assetPath)
    {
        if (AssetsPath is not { } root || Resolve(assetPath) is null)
            return false;
        if (new FileSoundLibrary(root).Stream(assetPath!) is not { } stream)
            return false;
        Start(stream, assetPath);
        return true;
    }

    /// <summary>Plays a sound, stopping any other preview.</summary>
    public void Preview(SoundClip clip, string? name = null) => Start(clip.Open(), name);

    /// <summary>Plays the preview of <paramref name="assetPath"/>, or stops it if it's the one playing.</summary>
    public void TogglePreview(string? assetPath)
    {
        if (IsPreviewing && string.Equals(Previewing, assetPath, StringComparison.OrdinalIgnoreCase))
            StopPreview();
        else
            Preview(assetPath);
    }

    public void StopPreview()
    {
        _preview?.Stop();
        _preview = null;
        Previewing = null;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the Sound Maker: on <paramref name="editing"/> (a sound made in it) to tweak, or on a new sound or tune.
    /// <paramref name="saved"/> is called with the asset path of each sound it saves.
    /// </summary>
    public SoundMakerWindow OpenMaker(SoundMakerMode mode = SoundMakerMode.Effect, string? editing = null, Action<string>? saved = null)
    {
        if (_maker is not null)
        {
            _maker.Model.Saved = saved;
            if (editing is not null)
                _maker.Model.Open(editing);
            else
                _maker.Model.Mode = mode;
            _maker.Activate();
            return _maker;
        }

        var model = new SoundMakerViewModel(this, mode) { Saved = saved };
        if (editing is not null)
            model.Open(editing);
        _maker = new SoundMakerWindow(model) { Owner = Application.Current?.MainWindow };
        _maker.Closed += (_, _) =>
        {
            _maker = null;
            StopPreview();
        };
        _maker.Show();
        return _maker;
    }

    /// <summary>The Sound Maker, while it's open.</summary>
    public SoundMakerWindow? Maker => _maker;

    /// <summary>Whether a sound file was made in the Sound Maker, so it can be opened there again.</summary>
    public bool CanEdit(string? assetPath) => Resolve(assetPath) is { } path &&
        (Wav.ReadSoundRecipe(path) is not null || Wav.ReadMusicRecipe(path) is not null);

    private void Start(IAudioStream stream, string? name)
    {
        _preview?.Stop();
        _preview = AudioOutput.Mixer.Play(stream, 0.9f);
        Previewing = name;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
}
