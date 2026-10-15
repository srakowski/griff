using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Prefabs;

/// <summary>
/// Puts open prefabs in the window's document tabs, after Scene and Game: one tab per prefab with its name, an
/// unsaved-changes dot and a close button (middle-click closes too). Picking a prefab's tab shows it in the panels and
/// moves the Scene view into it; picking the Scene tab shows the scene again. Prefab tabs are disabled while playing.
/// </summary>
public sealed class PrefabTabs(IWorkspace workspace)
{
    private TabControl? _tabs;
    private TabItem? _sceneTab;
    private FrameworkElement? _sceneView;

    /// <param name="sceneTab">The scene's tab, which holds <paramref name="sceneView"/> while the scene is showing.</param>
    public void Attach(TabControl tabs, TabItem sceneTab, FrameworkElement sceneView)
    {
        _tabs = tabs;
        _sceneTab = sceneTab;
        _sceneView = sceneView;
        ((INotifyCollectionChanged)workspace.Prefabs).CollectionChanged += OnPrefabsChanged;
        workspace.SceneShown += (_, _) =>
        {
            Follow();
            if (!workspace.IsPlaying && workspace.Scene is { IsPrefab: false })
                tabs.SelectedItem = sceneTab;
        };
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Follow();
            else if (e.PropertyName == nameof(IWorkspace.IsPlaying))
            {
                foreach (var tab in PrefabTabItems())
                    tab.IsEnabled = !workspace.IsPlaying;
            }
        };

        // Picking a tab shows what's in it; the Game tab and script tabs leave the panels as they are.
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || workspace.IsPlaying)
                return;
            if (tabs.SelectedItem == sceneTab)
                workspace.ShowScene();
            else if (tabs.SelectedItem is TabItem { Tag: SceneDocument prefab })
                workspace.Show(prefab);
        };
    }

    /// <summary>The tab showing <paramref name="prefab"/>, if it's open.</summary>
    public TabItem? TabFor(SceneDocument prefab) => PrefabTabItems().FirstOrDefault(t => t.Tag == prefab);

    // The Scene view goes where the document it shows is, and that tab comes to the front.
    private void Follow()
    {
        if (_tabs is null || _sceneTab is null || _sceneView is null)
            return;

        var showing = workspace.Scene is { IsPrefab: true } prefab ? TabFor(prefab) : null;
        var home = showing ?? _sceneTab;
        if (home.Content != _sceneView)
        {
            if (_sceneView.Parent is TabItem old)
                old.Content = null;
            home.Content = _sceneView;
        }

        if (showing is not null)
            _tabs.SelectedItem = showing;
        else if (_tabs.SelectedItem is TabItem { Tag: SceneDocument })
            _tabs.SelectedItem = _sceneTab;
    }

    private IEnumerable<TabItem> PrefabTabItems() =>
        _tabs?.Items.OfType<TabItem>().Where(t => t.Tag is SceneDocument) ?? [];

    private void OnPrefabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_tabs is null)
            return;

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in PrefabTabItems().ToList())
                _tabs.Items.Remove(tab);
        }

        foreach (SceneDocument prefab in e.OldItems ?? Array.Empty<SceneDocument>())
        {
            if (TabFor(prefab) is { } tab)
            {
                if (tab.Content == _sceneView)
                    tab.Content = null;
                _tabs.Items.Remove(tab);
            }
        }

        // New prefab tabs go after the last one (or after Scene and Game), before any scripts.
        foreach (SceneDocument prefab in e.NewItems ?? Array.Empty<SceneDocument>())
        {
            var tab = new TabItem { Tag = prefab, Header = Header(prefab), IsEnabled = !workspace.IsPlaying };
            tab.MouseUp += async (_, args) =>
            {
                if (args.ChangedButton == MouseButton.Middle)
                    await workspace.ClosePrefabAsync(prefab);
            };
            var last = PrefabTabItems().LastOrDefault();
            var index = last is not null ? _tabs.Items.IndexOf(last) + 1 : Math.Min(2, _tabs.Items.Count);
            _tabs.Items.Insert(index, tab);
        }

        Follow();
    }

    // [package icon] Coin [● or ×]: the dot shows unsaved changes and turns into the close button on hover.
    private StackPanel Header(SceneDocument prefab)
    {
        var icon = Icons.Create(Icons.Package, 12);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var name = new TextBlock { Text = prefab.Name, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var glyph = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(FrameworkElement.StyleProperty, "Style.Icon");
        var close = new Button
        {
            Content = glyph,
            Width = 18,
            Height = 18,
            Margin = new Thickness(8, 0, -6, 0),
            Focusable = false,
            ToolTip = "Close (middle-click)",
        };
        close.SetResourceReference(FrameworkElement.StyleProperty, "Style.Button.Toolbar");
        close.Click += async (_, _) => await workspace.ClosePrefabAsync(prefab);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent,
            ToolTip = $"The {prefab.Name} prefab: saving it updates every copy",
        };
        header.Children.Add(icon);
        header.Children.Add(name);
        header.Children.Add(close);

        void Update()
        {
            var hover = header.IsMouseOver;
            glyph.Text = prefab.IsDirty && !hover ? "●" : Icons.ChromeClose;
            glyph.FontFamily = prefab.IsDirty && !hover ? (FontFamily)Application.Current.FindResource("Font.UI") : (FontFamily)Application.Current.FindResource("Font.Icon");
            glyph.FontSize = prefab.IsDirty && !hover ? 12 : 9;
            close.Opacity = hover || prefab.IsDirty || (header.Parent is FrameworkElement { TemplatedParent: TabItem { IsSelected: true } }) ? 1 : 0.0;
        }

        header.MouseEnter += (_, _) => Update();
        header.MouseLeave += (_, _) => Update();
        ((INotifyPropertyChanged)prefab).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SceneDocument.IsDirty))
                Update();

            // Renaming the prefab's file renames its tab.
            if (e.PropertyName == nameof(SceneDocument.Name))
            {
                name.Text = prefab.Name;
                header.ToolTip = $"The {prefab.Name} prefab: saving it updates every copy";
            }
        };
        header.Loaded += (_, _) => Update();
        Update();
        return header;
    }
}
