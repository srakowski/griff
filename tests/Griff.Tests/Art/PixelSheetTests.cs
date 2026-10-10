using Griff.Art;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Art;

public class PixelSheetTests
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    [Fact]
    public void Cells_AreSeparate_AndDrawingNeverSpillsIntoTheNext()
    {
        var sheet = new PixelSheet(SheetLayout.Sprite(4, frames: 2));

        sheet.Line(0, 0, 1, 10, 1, Red);

        Assert.Equal(Red, sheet.Get(0, 3, 1));
        Assert.Equal(0u, sheet.Get(1, 0, 1));
        // In the whole picture, frame 1 starts four pixels across.
        Assert.Equal(8, sheet.Width);
        Assert.Equal(Red, sheet.Pixels[1 * 8 + 3]);
        Assert.Equal(0u, sheet.Pixels[1 * 8 + 4]);
    }

    [Fact]
    public void Fill_StopsAtEdgesOfOtherColors()
    {
        var sheet = new PixelSheet(SheetLayout.Sprite(5));
        sheet.Rectangle(0, 0, 0, 4, 4, Red, filled: false);

        sheet.Fill(0, 2, 2, Blue);

        Assert.Equal(Blue, sheet.Get(0, 1, 1));
        Assert.Equal(Blue, sheet.Get(0, 3, 3));
        Assert.Equal(Red, sheet.Get(0, 0, 0));
        Assert.Equal(Red, sheet.Get(0, 4, 2));
    }

    [Fact]
    public void FlipAndShift_MoveACellsPixels()
    {
        var sheet = new PixelSheet(SheetLayout.Sprite(4));
        sheet.Set(0, 0, 0, Red);

        sheet.Flip(0, horizontal: true);
        Assert.Equal(Red, sheet.Get(0, 3, 0));

        sheet.Shift(0, 1, 2);
        Assert.Equal(Red, sheet.Get(0, 0, 2));
    }

    [Fact]
    public void InsertingAndRemovingFrames_KeepsTheOthersInOrder()
    {
        var sheet = new PixelSheet(SheetLayout.Sprite(2, frames: 2));
        sheet.Set(0, 0, 0, Red);
        sheet.Set(1, 0, 0, Blue);

        sheet.InsertCell(1, copyOf: 0);

        Assert.Equal(3, sheet.Layout.Frames);
        Assert.Equal(3, sheet.Layout.Columns);
        Assert.Equal([Red, Red, Blue], new[] { sheet.Get(0, 0, 0), sheet.Get(1, 0, 0), sheet.Get(2, 0, 0) });

        sheet.RemoveCell(0);
        Assert.Equal(2, sheet.Layout.Frames);
        Assert.Equal([Red, Blue], new[] { sheet.Get(0, 0, 0), sheet.Get(1, 0, 0) });
    }

    [Fact]
    public void Relayout_KeepsCellsByNumber()
    {
        var sheet = new PixelSheet(SheetLayout.Tileset(2, columns: 2, rows: 2));
        sheet.Set(3, 1, 1, Red);

        sheet.Relayout(SheetLayout.Tileset(3, columns: 4, rows: 1));

        Assert.Equal(Red, sheet.Get(3, 1, 1));
        Assert.Equal(12, sheet.Width);
    }

    [Fact]
    public void Png_RoundTripsPixelsAndLayout()
    {
        var sheet = new PixelSheet(new SheetLayout(SheetKind.Sprite, 3, 5, 3, 1, 3, 12));
        sheet.Set(2, 2, 4, 0x80123456);
        sheet.Set(0, 0, 0, Red);

        var png = sheet.ToPng();
        var read = PixelSheet.FromPng(png);

        Assert.NotNull(read);
        Assert.Equal(sheet.Layout, read.Layout);
        Assert.True(sheet.SameAs(read));
        Assert.Equal(sheet.Layout.ToText(), Png.ReadText(png));
    }

    [Fact]
    public void PngFromElsewhere_IsCutUpByTheLayoutItsGiven()
    {
        var rgba = new byte[8 * 4 * 4];
        var png = Png.Encode(rgba, 8, 4);

        Assert.Null(Png.ReadText(png));
        var read = PixelSheet.FromPng(png, SheetLayout.Tileset(1, columns: 2, rows: 1));

        Assert.NotNull(read);
        Assert.Equal((4, 4), (read.Layout.CellWidth, read.Layout.CellHeight));
    }

    [Theory]
    [InlineData(SpriteIdea.Creature, 2)]
    [InlineData(SpriteIdea.Ship, 1)]
    [InlineData(SpriteIdea.Item, 1)]
    [InlineData(SpriteIdea.Ground, 1)]
    [InlineData(SpriteIdea.Bricks, 1)]
    public void Generator_MakesTheSamePictureFromTheSameSeed(SpriteIdea idea, int frames)
    {
        var first = SpriteGenerator.Generate(idea, 42);
        var again = SpriteGenerator.Generate(idea, 42);
        var other = SpriteGenerator.Generate(idea, 43);

        Assert.Equal(frames, first.Count);
        Assert.All(first, f => Assert.Equal(16 * 16, f.Length));
        Assert.Equal(first[0], again[0]);
        Assert.Contains(first[0], p => p != 0);
        Assert.NotEqual(first[0], other[0]);
    }

    [Fact]
    public void Generator_MirrorsCreaturesLeftToRight()
    {
        var creature = SpriteGenerator.Generate(SpriteIdea.Creature, 7)[0];

        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 8; x++)
                Assert.Equal(creature[y * 16 + x], creature[y * 16 + 15 - x]);
    }

    [Fact]
    public void UseSheet_SetsTheSheetAndAddsAnAnimation_AsOneUndoStep()
    {
        var registry = ComponentRegistry.CreateDefault();
        var entity = new Entity("Slime");
        var sprite = entity.AddComponent<Sprite>();
        var history = new EditHistory();

        var edit = ArtEdits.UseSheet(registry, sprite, "Image", "Sprites/slime.png", SheetLayout.Sprite(16, frames: 2, fps: 6));
        Assert.NotNull(edit);
        history.Execute(edit);

        Assert.Equal("Sprites/slime.png", sprite.Image);
        Assert.Equal((2, 1), (sprite.Columns, sprite.Rows));
        var animation = entity.GetComponent<Animation>();
        Assert.NotNull(animation);
        Assert.Equal((0, 2, 6f), (animation.FirstFrame, animation.FrameCount, animation.FramesPerSecond));

        history.Undo();
        Assert.Null(sprite.Image);
        Assert.Null(entity.GetComponent<Animation>());
    }

    [Fact]
    public void UseSheet_ClearsAPlaceholderBoxesTint_ButNotARealSpritesTint()
    {
        var registry = ComponentRegistry.CreateDefault();
        var box = new Entity("Slime").AddComponent<Sprite>();
        box.Tint = Color.FromBytes(0x57, 0xAB, 0x5A);
        ArtEdits.UseSheet(registry, box, "Image", "Sprites/slime.png", SheetLayout.Sprite())!.Apply();
        Assert.Equal(Color.White, box.Tint);

        box.Tint = Color.FromBytes(0xFF, 0, 0);
        ArtEdits.UseSheet(registry, box, "Image", "Sprites/slime2.png", SheetLayout.Sprite())!.Apply();
        Assert.Equal(Color.FromBytes(0xFF, 0, 0), box.Tint);
    }

    [Fact]
    public void UseSheet_GivesATilemapItsTileGrid()
    {
        var registry = ComponentRegistry.CreateDefault();
        var tilemap = new Entity("Level").AddComponent<Tilemap>();

        var edit = ArtEdits.UseSheet(registry, tilemap, "Image", "Tiles/cave.png", SheetLayout.Tileset(16, columns: 4, rows: 2));
        edit!.Apply();

        Assert.Equal(("Tiles/cave.png", 4, 2), (tilemap.Image, tilemap.Columns, tilemap.Rows));
        Assert.Null(ArtEdits.UseSheet(registry, tilemap, "Image", "Tiles/cave.png", SheetLayout.Tileset(16, columns: 4, rows: 2)));
    }
}
