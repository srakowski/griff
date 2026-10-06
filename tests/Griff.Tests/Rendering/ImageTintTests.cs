using Griff.Rendering;

namespace Griff.Tests.Rendering;

public class ImageTintTests
{
    [Fact]
    public void White_IsIdentity_WhateverItsAlpha()
    {
        Assert.True(ImageTint.IsIdentity(Color.White));
        Assert.True(ImageTint.IsIdentity(Color.White with { A = 0.3f }));
        Assert.False(ImageTint.IsIdentity(new Color(1, 0.5f, 1)));
    }

    [Fact]
    public void ApplyBgra_MultipliesColorChannels_AndKeepsAlpha()
    {
        // One BGRA pixel: blue 200, green 100, red 255, alpha 128.
        byte[] pixels = [200, 100, 255, 128];

        ImageTint.ApplyBgra(pixels, 0xFF8000); // full red, half green, no blue

        Assert.Equal([0, 50, 255, 128], pixels);
    }

    [Fact]
    public void Quantize_RoundsCloseTintsTogether_AndKeepsTheEnds()
    {
        Assert.Equal(0xFFFFFF, ImageTint.Quantize(Color.White));
        Assert.Equal(0x000000, ImageTint.Quantize(Color.Black));
        Assert.Equal(ImageTint.Quantize(Color.FromBytes(200, 10, 90)), ImageTint.Quantize(Color.FromBytes(201, 11, 91)));
        Assert.Equal(0xFF0000, ImageTint.Quantize(new Color(1, 0, 0, 0.5f)));
    }

    [Fact]
    public void Quantize_HasAtMostLevelsStepsPerChannel()
    {
        var steps = Enumerable.Range(0, 256).Select(v => ImageTint.Quantize(Color.FromBytes((byte)v, 0, 0)) >> 16).Distinct().Count();

        Assert.Equal(ImageTint.Levels, steps);
    }
}
