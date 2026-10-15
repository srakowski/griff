using Griff.Input;
using Griff.Web;

namespace Griff.Tests.Web;

/// <summary>The web player's keys: the browser's KeyboardEvent.code as Griff's. (Its frames are DrawList's, see DrawListTests.)</summary>
public sealed class WebPlayerTests
{
    [Theory]
    [InlineData("KeyA", Key.A)]
    [InlineData("KeyZ", Key.Z)]
    [InlineData("Digit0", Key.D0)]
    [InlineData("Digit7", Key.D7)]
    [InlineData("Numpad3", Key.NumPad3)]
    [InlineData("F1", Key.F1)]
    [InlineData("F12", Key.F12)]
    [InlineData("ArrowLeft", Key.Left)]
    [InlineData("ArrowUp", Key.Up)]
    [InlineData("Space", Key.Space)]
    [InlineData("ShiftRight", Key.RightShift)]
    [InlineData("NumpadEnter", Key.Enter)]
    [InlineData("Backquote", Key.Grave)]
    [InlineData("F13", Key.None)]
    [InlineData("Keya", Key.None)]
    [InlineData("MetaLeft", Key.None)]
    [InlineData("", Key.None)]
    [InlineData(null, Key.None)]
    public void Keys_AreTheBrowsersCodes(string? code, Key key) => Assert.Equal(key, WebKeys.ToGriff(code));

    [Fact]
    public void EveryGriffKey_HasABrowserCode()
    {
        string[] named =
        [
            "Space", "Enter", "Escape", "Tab", "Backspace", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "ShiftLeft",
            "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "Delete", "Insert", "Home", "End", "PageUp",
            "PageDown", "CapsLock", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadDecimal", "Minus",
            "Equal", "Comma", "Period", "Slash", "Backslash", "Semicolon", "Quote", "BracketLeft", "BracketRight", "Backquote",
        ];
        var codes = named
            .Concat(Enumerable.Range('A', 26).Select(c => $"Key{(char)c}"))
            .Concat(Enumerable.Range(0, 10).Select(d => $"Digit{d}"))
            .Concat(Enumerable.Range(0, 10).Select(d => $"Numpad{d}"))
            .Concat(Enumerable.Range(1, 12).Select(f => $"F{f}"));

        var reached = codes.Select(WebKeys.ToGriff).ToHashSet();

        var unreached = Enum.GetValues<Key>().Where(k => k != Key.None && !reached.Contains(k)).ToList();
        Assert.True(unreached.Count == 0, "No browser code for: " + string.Join(", ", unreached));
    }
}
