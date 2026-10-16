namespace Griff.Templates;

// A crate, painted for the Contraption template's props and shared by the Puzzle and Multiplayer templates.
internal static partial class TemplateArt
{
    public const string CrateArt = "Textures/crate.png";

    internal static Canvas Crate()
    {
        var c = new Canvas(32, 32, 40);
        var wood = Rgb(0xA8, 0x74, 0x3E);
        c.Fill(wood);
        c.Bevel(0, 0, 32, 32, wood.Times(1.25f), wood.Times(0.55f));
        c.Rect(3, 3, 26, 3, wood.Times(0.75f));
        c.Rect(3, 26, 26, 3, wood.Times(0.75f));
        c.Line(5, 25, 26, 6, wood.Times(0.7f));
        c.Noise(0.06f);
        return c;
    }
}
