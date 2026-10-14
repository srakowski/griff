using Griff.Scripting;

namespace Griff.Tests.Scripting;

/// <summary>A line of one version of a script is found in another: lines added or taken out above it move it, a line changed in place stays.</summary>
public class LineMapTests
{
    private const string Before = "a\nb\nc\nd\ne";

    [Fact]
    public void The_same_text_maps_every_line_to_itself() => Assert.Equal(3, LineMap.Map(Before, Before, 3));

    [Fact]
    public void Lines_added_or_taken_out_above_move_it()
    {
        Assert.Equal(5, LineMap.Map(Before, "x\ny\na\nb\nc\nd\ne", 3));
        Assert.Equal(1, LineMap.Map(Before, "c\nd\ne", 3));
        Assert.Equal(3, LineMap.Map("x\ny\na\nb\nc\nd\ne", Before, 5));
    }

    [Fact]
    public void A_line_changed_in_place_stays_where_it_was()
    {
        Assert.Equal(3, LineMap.Map(Before, "a\nb\nC!\nd\ne", 3));
        Assert.Equal(4, LineMap.Map("a\nb\nC!\nd\ne", "x\na\nb\nc\nd\ne", 3));
    }

    [Fact]
    public void A_line_only_one_has_goes_to_the_next_line_both_have()
    {
        Assert.Equal(3, LineMap.Map("a\nb\nnew\nc\nd\ne", Before, 3));
        Assert.Null(LineMap.Map("a\nb\nc\nd\ne\nnew", Before, 6));
        Assert.Null(LineMap.Map(Before, Before + "\nmore", 9));
    }

    [Fact]
    public void Windows_and_unix_line_endings_read_the_same() =>
        Assert.Equal(3, LineMap.Map("a\r\nb\r\nc\r\nd", "x\na\nb\nc\nd", 2));
}
