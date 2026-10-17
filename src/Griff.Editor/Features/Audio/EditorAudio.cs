using System.IO;
using Griff.Audio;
using Griff.Editor.Core.Workspace;
using Griff.Platform.Windows;

namespace Griff.Editor.Features.Audio;

/// <summary>
/// Sound in the editor: previews of sound files and freshly made sounds (one at a time), the project's sounds for
/// pickers, and the Sound Maker. Previews play through the editor's sound output, as its games do.
/// </summary>
public sealed class EditorAudio
{
    private readonly IProjectSession _projectSession;
    private Voice? _preview;

    private readonly Lazy<Core.Documents.EditorDocuments> _documents;
    private readonly SoundOutput _sound;

    /// <summary>What the Sound Maker's tabs are called ("Sound Maker: coin.wav").</summary>
    public const string MakerKind = "Sound Maker";

    public EditorAudio(IProjectSession projectSession, Lazy<Core.Documents.EditorDocuments> documents, SoundOutput sound)
    {
        _projectSession = projectSession;
        _documents = documents;
        _sound = sound;
    }

    public static readonly string[] Extensions = [".wav", ".mp3", ".ogg"];

    /// <summary>The sound or music file being previewed, by asset path, while it plays.</summary>
    public string? Previewing { get; private set; }

    public bool IsPreviewing => _preview?.IsPlaying == true;

    /// <summary>Raised when a preview starts or stops being asked for.</summary>
    public event EventHandler? PreviewChanged;

    public string? AssetsPath => _projectSession.Project?.AssetsPath;

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
        if (WindowsAudio.Library(new Griff.Projects.AssetFolder(root)).Stream(assetPath!) is not { } stream)
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
    /// Opens the Sound Maker in a tab of its own in the document area: on <paramref name="editing"/> (a sound made in it)
    /// to tweak, bringing its tab forward if it's open already, or on a new sound or tune. <paramref name="saved"/> is
    /// called with the asset path of each sound it saves.
    /// </summary>
    public SoundMakerViewModel OpenMaker(SoundMakerMode mode = SoundMakerMode.Effect, string? editing = null, Action<string>? saved = null)
    {
        var documents = _documents.Value;
        if (editing is not null && documents.Open.FirstOrDefault(d => d.Kind == MakerKind && string.Equals(((SoundMakerViewModel)d.Model).Editing, editing, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            var making = (SoundMakerViewModel)open.Model;
            making.Saved = saved;
            documents.Show(open);
            return making;
        }

        var model = new SoundMakerViewModel(this, mode) { Saved = saved };
        if (editing is not null)
            model.Open(editing);
        var document = new Core.Documents.EditorDocument(MakerKind, model, () =>
        {
            var view = new SoundMakerView(model);
            view.CloseRequested += (_, _) => documents.Close(documents.Of(model)!);
            return view;
        }, model.IsMusic ? Themes.Icons.Music : Themes.Icons.Audio)
        {
            Save = () => model.Save() is not null,
        };

        // Its tab says what it's making and whether that's saved.
        void Follow()
        {
            document.Title = $"{model.Name.Trim()}.wav";
            document.Key = model.Editing;
            document.IsDirty = model.IsDirty;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SoundMakerViewModel.Name) or nameof(SoundMakerViewModel.Editing) or nameof(SoundMakerViewModel.IsDirty) or "" or null)
                Follow();
        };
        Follow();
        document.Closed += (_, _) => StopPreview();
        documents.Show(document);
        return model;
    }

    /// <summary>The Sound Maker in front (or opened last), while one is open.</summary>
    public SoundMakerViewModel? Maker =>
        _documents.Value.Showing is { Kind: MakerKind, Model: SoundMakerViewModel showing } ? showing
        : _documents.Value.Open.LastOrDefault(d => d.Kind == MakerKind)?.Model as SoundMakerViewModel;

    /// <summary>Whether a sound file was made in the Sound Maker, so it can be opened there again.</summary>
    public bool CanEdit(string? assetPath) => Resolve(assetPath) is { } path &&
        (Wav.ReadSoundRecipe(path) is not null || Wav.ReadMusicRecipe(path) is not null);

    private void Start(IAudioStream stream, string? name)
    {
        _preview?.Stop();
        _sound.Start();
        _preview = _sound.Mixer.Play(stream, 0.9f);
        Previewing = name;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
}
