#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Scripts;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

// The script editor: colors, the component panel, completion, hover info, signature help and live errors.
internal static partial class SnapshotRunner
{
    private const string ShipSource = """
        /// <summary>Flies the ship with the arrow keys and fires with Space.</summary>
        [Category("Player")]
        [Requires(typeof(Body), Note = "moves by velocity")]
        public class Ship : Behavior
        {
            /// <summary>How fast it flies, in units a second.</summary>
            public float Speed { get; set; } = 6;

            /// <summary>Shots a second while Space is held.</summary>
            public float FireRate { get; set; } = 4;

            /// <summary>Tints the ship.</summary>
            public Color Tint { get; set; } = Color.White;

            private float _cooldown;

            protected override void Update()
            {
                var body = GetComponent<Body>()!;
                body.Velocity = new Vector2(Input.Horizontal, Input.Vertical) * Speed;
                _cooldown -= Time.Delta;
                if (Input.IsDown(Key.Space) && _cooldown <= 0)
                {
                    _cooldown = 1 / FireRate;
                    Audio.Play("Sounds/shoot.wav");
                }
            }
        }

        """;

    private static async Task ScriptEditorSnapshots(IServiceProvider services, Window main, IWorkspace workspace, string outputDir)
    {
        var checks = new List<string>();
        var path = Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Ship.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ShipSource.Replace("\n", Environment.NewLine));
        var compiled = workspace.ReloadScripts();
        checks.Add($"ship compiles: {(compiled?.Success == true ? "ok" : "FAILED " + string.Join("; ", compiled?.Diagnostics ?? []))}");

        var ship = workspace.Scene!.Scene.Roots.First();
        if (workspace.Components.Find("Ship") is { } descriptor)
            workspace.Scene.Execute(ComponentCatalog.AddWithRequirements(workspace.Components, descriptor, ship));
        services.GetRequiredService<ISelection>().Select(ship);

        var editors = services.GetRequiredService<ScriptEditors>();
        var document = editors.Open(path)!;
        await Settle(main, 2500);
        var view = services.GetRequiredService<ScriptTabs>().ViewFor(document)!;
        var editor = view.Editor;
        var text = document.Text.Text;
        editor.GoTo(text.IndexOf("Speed {", StringComparison.Ordinal) + 2);
        await Settle(main, 600);
        Render(main, Path.Combine(outputDir, "script-editor.png"));
        checks.Add($"semantic colors: {(document.Classifications.Count > 20 ? "ok" : "FAILED")} ({document.Classifications.Count} spans)");
        checks.Add($"component panel shows Ship: {(view.Component.IsComponent && view.Component.Rows.Count == 3 ? "ok" : "FAILED")} ({view.Component.Title}, {view.Component.Rows.Count} rows)");
        checks.Add($"panel has live values for {ship.Name}: {(view.Component.Rows.All(r => r.HasField) ? "ok" : "FAILED")}");
        checks.Add($"the caret's property lights up: {(view.Component.Rows.FirstOrDefault(r => r.IsActive)?.Name == "Speed" ? "ok" : "FAILED")}");

        // Completion after a dot.
        var at = text.IndexOf("_cooldown -= Time.Delta;", StringComparison.Ordinal);
        document.Text.Insert(at, "Input." + Environment.NewLine + "        ");
        editor.CaretOffset = at + "Input.".Length;
        await editor.ShowCompletionAsync('.');
        await Settle(main, 500);
        var list = editor.CompletionWindow;
        checks.Add($"completion lists Input members: {(list?.CompletionList.CompletionData.Any(d => d.Text == "Horizontal") == true ? "ok" : "FAILED")}");
        if (list is not null)
        {
            list.CompletionList.SelectItem("IsDo");
            await Settle(main, 300);
            Render(list, Path.Combine(outputDir, "script-completion.png"));
            list.Close();
        }

        // Live errors: Input. on its own doesn't compile.
        await Settle(main, 900);
        Render(main, Path.Combine(outputDir, "script-errors.png"));
        checks.Add($"live errors: {(document.ErrorCount > 0 ? "ok" : "FAILED")} ({document.ErrorCount} errors)");
        document.Text.UndoStack.Undo();
        await Settle(main, 900);
        checks.Add($"errors clear after undo: {(document.ErrorCount == 0 ? "ok" : "FAILED")} ({document.ErrorCount})");

        // Hover info on a Griff API.
        text = document.Text.Text;
        await editor.ShowQuickInfoAsync(text.IndexOf("Delta", StringComparison.Ordinal) + 1);
        await Settle(main, 300);
        var hoverText = editor.QuickInfo is { } shown ? string.Join(" / ", Demo.DemoUi.Descendants<System.Windows.Controls.TextBlock>(shown).Select(t => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)))) : "";
        checks.Add($"hover info with docs: {(hoverText.Contains("Seconds since the previous frame") ? "ok" : "FAILED")} ({hoverText})");
        if (editor.QuickInfo is { } info)
            RenderElement(info, Path.Combine(outputDir, "script-quickinfo.png"));
        editor.CloseToolTip();

        // Signature help inside a call.
        editor.CaretOffset = text.IndexOf("Input.Vertical", StringComparison.Ordinal);
        await editor.ShowSignatureHelpAsync(force: true);
        await Settle(main, 300);
        checks.Add($"signature help: {(editor.SignatureHelp is not null ? "ok" : "FAILED")}");
        if (editor.SignatureHelp is { } signature)
            RenderElement(signature, Path.Combine(outputDir, "script-signature.png"));

        // Find and replace.
        view.OpenFind(replace: true);
        await Settle(main, 300);
        Render(main, Path.Combine(outputDir, "script-find.png"));

        File.WriteAllLines(Path.Combine(outputDir, "checks-scripts.txt"), checks);
        editors.Close(document);
    }

    private static async Task Settle(Window main, int ms)
    {
        await Task.Delay(ms);
        main.UpdateLayout();
        await main.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // A popup's content: hover info, signature help.
    private static void RenderElement(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth + element.Margin.Left + element.Margin.Right));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight + element.Margin.Top + element.Margin.Bottom));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
