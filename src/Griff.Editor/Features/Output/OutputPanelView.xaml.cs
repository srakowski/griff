using System.Collections.Specialized;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Scripts;

namespace Griff.Editor.Features.Output;

public partial class OutputPanelView : UserControl
{
    private readonly IWorkspace _workspace;
    private readonly ScriptEditors _scripts;

    public OutputPanelView(IWorkspace workspace, ScriptEditors scripts)
    {
        InitializeComponent();
        _workspace = workspace;
        _scripts = scripts;
        ((INotifyCollectionChanged)EntryList.Items).CollectionChanged += OnItemsChanged;
        EntryList.MouseDoubleClick += OnEntryOpened;
        EntryList.ToolTip = "Double-click a script error to go to it";
    }

    // Keep the newest entry in view, like a console.
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[^1]);
    }

    // "Ship.cs(12,5): ; expected" opens Ship.cs in the script editor at line 12.
    private void OnEntryOpened(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is not LogEntry entry || ScriptLine().Match(entry.Message) is not { Success: true } match ||
            _workspace.Project is not { } project || !Directory.Exists(project.AssetsPath))
            return;
        var file = Directory.EnumerateFiles(project.AssetsPath, match.Groups["file"].Value, SearchOption.AllDirectories).FirstOrDefault();
        if (file is not null)
            _scripts.Open(file, int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["column"].Value));
    }

    [GeneratedRegex(@"^(?<file>[^\/:*?""<>|()]+\.cs)\((?<line>\d+),(?<column>\d+)\)")]
    private static partial Regex ScriptLine();
}
