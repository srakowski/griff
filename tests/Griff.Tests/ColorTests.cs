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
}
