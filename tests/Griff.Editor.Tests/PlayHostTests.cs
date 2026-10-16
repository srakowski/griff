using System.IO;
using System.Windows.Threading;
using Griff.Audio;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Play;
using Griff.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The editor hosts its games as every host does: made by <c>LoadedGame.NewGame</c>, played by the core's loop, and heard
/// through the editor's own sound output (a service of its own, not a static of the process), as its previews are.
/// </summary>
public sealed class PlayHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-playhost-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Play_makes_the_game_as_a_host_does_and_sounds_through_the_editors_output() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Hosted"));
        var tone = new SoundClip(Enumerable.Repeat(0.5f, Mixer.SampleRate / 5).ToArray(), 1, Mixer.SampleRate);
        File.WriteAllBytes(Path.Combine(workspace.Project!.AssetsPath, "beep.wav"), Wav.Encode(tone));
        var sound = services.GetRequiredService<SoundOutput>();
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        try
        {
            var game = play.Runner!.Game;
            Assert.Same(sound, play.Sound);
            Assert.Same(play.Loop, play.Runner.Loop);
            Assert.Equal("Hosted", game.Title);
            Assert.NotNull(game.SceneLoader);
            Assert.NotNull(game.PrefabLoader);
            Assert.Same(services.GetRequiredService<IProjectSession>().Components, game.Components);

            // The game's sound plays on the editor's mixer, as a preview does; muted (as tests are), nothing reaches a device.
            var playing = sound.Mixer.Playing;
            game.Audio.Play("beep.wav");
            Assert.Equal(playing + 1, sound.Mixer.Playing);
            services.GetRequiredService<EditorAudio>().Preview("beep.wav");
            Assert.Equal(playing + 2, sound.Mixer.Playing);
            Assert.True(sound.Muted);
            Assert.False(sound.IsDeviceOpen);
        }
        finally
        {
            services.GetRequiredService<EditorAudio>().StopPreview();
            play.Stop();
        }
    });

    [Fact]
    public void No_sound_output_is_a_static_of_the_process()
    {
        var shared = typeof(SoundOutput).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .Where(f => f.FieldType == typeof(SoundOutput) || f.FieldType == typeof(Mixer))
            .Select(f => $"{f.DeclaringType!.FullName}.{f.Name}")
            .ToList();

        Assert.True(shared.Count == 0, "Each host keeps its own sound output (AudioOutput.Open):\n" + string.Join("\n", shared));
    }
}
