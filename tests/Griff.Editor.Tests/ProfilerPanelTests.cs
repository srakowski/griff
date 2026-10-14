using System.Windows.Threading;
using Griff.Editor.Core.Panels;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Profiler;
using Griff.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Profiler panel times the game only while it's on screen and recording, so out of sight it costs nothing; its
/// commands show it, pause it and clear it, for agents too.
/// </summary>
public sealed class ProfilerPanelTests
{
    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void The_game_is_timed_only_while_the_panel_is_on_screen_and_recording() => EditorHost.Run(services =>
    {
        var play = services.GetRequiredService<PlayMode>();
        var profiler = services.GetRequiredService<ProfilerPanel>();
        Assert.Null(play.Profiler);
        Assert.False(profiler.IsWatching);
        Assert.Equal("Play the game to see how it runs.", profiler.Hint);

        profiler.IsOnScreen = true;
        Assert.True(profiler.IsWatching);
        Assert.Same(profiler.Frames, play.Profiler);

        var record = services.GetRequiredService<RecordProfilerCommand>();
        Assert.True(record.IsChecked);
        Wait(record.ExecuteAsync());
        Assert.False(profiler.IsRecording);
        Assert.False(record.IsChecked);
        Assert.Null(play.Profiler);
        Assert.Equal("Recording is paused.", profiler.Hint);
        Wait(record.ExecuteAsync());
        Assert.Same(profiler.Frames, play.Profiler);

        profiler.IsOnScreen = false;
        Assert.Null(play.Profiler);
    });

    [Fact]
    public void It_sums_up_the_frames_recorded_and_clears_them() => EditorHost.Run(services =>
    {
        var profiler = services.GetRequiredService<ProfilerPanel>();
        var changed = new List<string?>();
        profiler.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        for (var i = 0; i < 10; i++)
            profiler.Frames.Add(new FrameSample(20, 3, 4, 1, 1, 50));
        profiler.Refresh();
        Assert.Equal(50, profiler.Summary.Fps, 3);
        Assert.Equal(3, profiler.Summary.StepMs, 3);
        Assert.Equal(50, profiler.Summary.Draws, 3);
        Assert.Equal(10, profiler.Version);
        Assert.Contains(nameof(ProfilerPanel.Version), changed);
        Assert.Null(profiler.Hint);

        Wait(services.GetRequiredService<ClearProfilerCommand>().ExecuteAsync());
        Assert.Equal(0, profiler.Frames.Count);
        Assert.Same(FrameSummary.None, profiler.Summary);
        Assert.Same(SceneCounts.None, profiler.Counts);
    });

    [Fact]
    public void Game_Profiler_brings_the_panel_to_the_front_of_its_tabs() => EditorHost.Run(services =>
    {
        var layout = services.GetRequiredService<PanelLayout>();
        Assert.Contains("Profiler", layout.Summary);
        Assert.NotEqual("Profiler", layout.GroupOf("Profiler").Selected);

        Wait(services.GetRequiredService<ShowProfilerCommand>().ExecuteAsync());
        Assert.Equal("Profiler", layout.GroupOf("Profiler").Selected);

        // Agents reach it as tools, as they do every command.
        var tools = services.GetRequiredService<AgentServer>().Tools;
        Assert.Contains("Game_Profiler", tools.Keys);
        Assert.Contains("Profiler_Record", tools.Keys);
        Assert.Contains("Profiler_Clear", tools.Keys);
    });
}
