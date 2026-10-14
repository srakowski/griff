using System.Windows;
using System.Windows.Controls;

namespace Griff.Editor.Features.Profiler;

public partial class ProfilerPanelView : UserControl
{
    public ProfilerPanelView(RecordProfilerCommand record, ClearProfilerCommand clear)
    {
        InitializeComponent();
        RecordButton.Command = record;
        ClearButton.Command = clear;

        // The game is timed only while the panel is on screen.
        IsVisibleChanged += (_, _) => OnScreenChanged();
        DataContextChanged += (_, _) => OnScreenChanged();
    }

    private void OnScreenChanged()
    {
        if (DataContext is ProfilerPanel panel)
            panel.IsOnScreen = IsVisible;
    }
}
