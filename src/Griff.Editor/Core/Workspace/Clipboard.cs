using System.Runtime.InteropServices;
using System.Windows;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The clipboard the editor copies to and pastes from: Windows' own, shared with every other program, so entities and
/// table cells copied in Griff paste elsewhere and text copied elsewhere pastes into Griff.
/// </summary>
public interface IClipboard
{
    /// <summary>The text on the clipboard; empty with none, or while another program holds the clipboard.</summary>
    string GetText();

    /// <summary>Puts <paramref name="text"/> on the clipboard; false if another program held it and it couldn't be.</summary>
    bool SetText(string text);

    /// <summary>What's on the clipboard in <paramref name="format"/> (one of Griff's own), or null.</summary>
    string? Get(string format);

    /// <summary>
    /// Puts <paramref name="data"/> on the clipboard in <paramref name="format"/>, and as text for other programs; false
    /// if another program held it and it couldn't be.
    /// </summary>
    bool Set(string format, string data);
}

public sealed class WindowsClipboard : IClipboard
{
    public string GetText() => Try(() => Clipboard.ContainsText() ? Clipboard.GetText() : "") ?? "";

    public bool SetText(string text) => Try(() =>
    {
        Clipboard.SetDataObject(text, copy: true);
        return true;
    });

    public string? Get(string format) => Try(() => Clipboard.ContainsData(format) ? Clipboard.GetData(format) as string : null);

    public bool Set(string format, string data) => Try(() =>
    {
        var both = new DataObject();
        both.SetData(format, data);
        both.SetText(data);
        Clipboard.SetDataObject(both, copy: true);
        return true;
    });

    // Another program can hold the clipboard open for a moment: a few tries, then it counts as empty (or the copy is lost).
    private static T? Try<T>(Func<T> use)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return use();
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(20);
            }
            catch (ExternalException)
            {
                return default;
            }
        }
    }
}
