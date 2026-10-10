using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Scripts;

/// <summary>
/// Puts open scripts in the window's document tabs, after Scene and Game: one tab per script with its name, an
/// unsaved-changes dot and a close button (middle-click closes too).
/// </summary>
public sealed class ScriptTabs(ScriptEditors editors, IServiceProvider services)
{
    private TabControl? _tabs;

    public void Attach(TabControl tabs)
    {
        _tabs = tabs;
        editors.Documents.CollectionChanged += OnDocumentsChanged;
        editors.ShowRequested += (_, document) =>
        {
            if (Find(document) is { } tab)
                tabs.SelectedItem = tab;
        };
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == tabs)
                editors.Active = (tabs.SelectedItem as TabItem)?.Tag as ScriptDocument;
        };
    }

    /// <summary>The view showing a script, if its tab exists.</summary>
    public ScriptEditorView? ViewFor(ScriptDocument document) => Find(document)?.Content as ScriptEditorView;

    /// <summary>Closes completion lists, signature help and hover info in every script tab.</summary>
    public void ClosePopups()
    {
        foreach (var view in _tabs?.Items.OfType<TabItem>().Select(t => t.Content).OfType<ScriptEditorView>() ?? [])
            view.Editor.ClosePopups();
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_tabs is null)
            return;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in _tabs.Items.OfType<TabItem>().Where(t => t.Tag is ScriptDocument).ToList())
                _tabs.Items.Remove(tab);
            return;
        }

        foreach (ScriptDocument document in e.OldItems ?? Array.Empty<ScriptDocument>())
        {
            if (Find(document) is { } tab)
            {
                var wasSelected = tab.IsSelected;
                var index = _tabs.Items.IndexOf(tab);
                _tabs.Items.Remove(tab);
                if (wasSelected)
                    _tabs.SelectedIndex = Math.Min(index, _tabs.Items.Count - 1);
            }
        }

        foreach (ScriptDocument document in e.NewItems ?? Array.Empty<ScriptDocument>())
        {
            var view = ActivatorUtilities.CreateInstance<ScriptEditorView>(services, document);
            var tab = new TabItem { Tag = document, Content = view, Header = Header(document) };
            tab.MouseUp += (_, args) =>
            {
                if (args.ChangedButton == MouseButton.Middle)
                    editors.Close(document);
            };
            _tabs.Items.Add(tab);
        }
    }

    private TabItem? Find(ScriptDocument document) =>
        _tabs?.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == document);

    // [code icon] Ship.cs [● or ×]: the dot shows unsaved changes and turns into the close button on hover.
    private FrameworkElement Header(ScriptDocument document)
    {
        var icon = Icons.Create(Icons.Code, 12);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var name = new TextBlock { Text = document.FileName, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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
        close.Click += (_, _) => editors.Close(document);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent };
        header.Children.Add(icon);
        header.Children.Add(name);
        header.Children.Add(close);

        void Update()
        {
            var hover = header.IsMouseOver;
            glyph.Text = document.IsDirty && !hover ? "●" : Icons.ChromeClose;
            glyph.FontFamily = document.IsDirty && !hover ? (System.Windows.Media.FontFamily)Application.Current.FindResource("Font.UI") : (System.Windows.Media.FontFamily)Application.Current.FindResource("Font.Icon");
            glyph.FontSize = document.IsDirty && !hover ? 12 : 9;
            close.Opacity = hover || document.IsDirty || (header.Parent is FrameworkElement { TemplatedParent: TabItem { IsSelected: true } }) ? 1 : 0.0;
        }

        header.MouseEnter += (_, _) => Update();
        header.MouseLeave += (_, _) => Update();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptDocument.IsDirty))
                Update();
        };
        header.Loaded += (_, _) => Update();
        Update();
        return header;
    }
}
