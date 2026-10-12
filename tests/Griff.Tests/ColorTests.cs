namespace Griff.Tests;

public class ColorTests
{
    [Theory]
    [InlineData("#F08C2EFF", 0xF0, 0x8C, 0x2E, 0xFF)]
    [InlineData("F08C2E", 0xF0, 0x8C, 0x2E, 0xFF)]
    [InlineData("#fff", 0xFF, 0xFF, 0xFF, 0xFF)]
    [InlineData("#00000080", 0, 0, 0, 0x80)]
    public void TryParse_AcceptsCommonForms(string text, byte r, byte g, byte b, byte a)
    {
        Assert.True(Color.TryParse(text, out var color));
        Assert.Equal((r, g, b, a), (color.RByte, color.GByte, color.BByte, color.AByte));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    public void TryParse_RejectsInvalid(string? text)
    {
        Assert.False(Color.TryParse(text, out _));
    }

    [Fact]
    public void ToHex_RoundTrips()
    {
        var color = Color.FromBytes(0x12, 0x34, 0x56, 0x78);

        Assert.Equal("#12345678", color.ToHex());
        Assert.True(Color.TryParse(color.ToHex(), out var parsed));
        Assert.Equal(color, parsed);
    }

    [Fact]
    public void ToHex_ClampsOutOfRangeChannels()
    {
        Assert.Equal("#FF0000FF", new Color(2f, -1f, 0f).ToHex());
    }

    [Theory]
    [InlineData(0, 1, 1, "#FF0000FF")]
    [InlineData(120, 1, 1, "#00FF00FF")]
    [InlineData(240, 1, 1, "#0000FFFF")]
    [InlineData(60, 1, 1, "#FFFF00FF")]
    [InlineData(300, 0.5, 0.5, "#804080FF")]
    [InlineData(720, 1, 1, "#FF0000FF")]
    [InlineData(-120, 1, 1, "#0000FFFF")]
    [InlineData(200, 0, 0.5, "#808080FF")]
    public void FromHsv_GoesRoundTheColorWheel(float hue, float saturation, float value, string hex)
    {
        Assert.Equal(hex, Color.FromHsv(hue, saturation, value).ToHex());
    }

    [Theory]
    [InlineData("#FF004DFF")]
    [InlineData("#29ADFFFF")]
    [InlineData("#5F574F80")]
    [InlineData("#000000FF")]
    [InlineData("#FFFFFFFF")]
    public void ToHsv_RoundTripsThroughFromHsv(string hex)
    {
        Assert.True(Color.TryParse(hex, out var color));
        var (hue, saturation, value) = color.ToHsv();

        Assert.InRange(hue, 0, 360);
        Assert.Equal(hex, Color.FromHsv(hue, saturation, value, color.A).ToHex());
    }

    [Fact]
    public void ToHsv_GivesGraysNoHueOrSaturation()
    {
        Assert.Equal((0f, 0f, 0.5f), new Color(0.5f, 0.5f, 0.5f).ToHsv());
        Assert.Equal((0f, 0f, 0f), Color.Black.ToHsv());
    }
}
