using Griff.Data;

namespace Griff.Tests.Data;

/// <summary>Tables, their typed columns and cells, dice, their files (written to read and diff well) and CSV and spreadsheet text.</summary>
public sealed class TableTests
{
    private static Table Spells() => new("Spells",
        [
            new TableColumn("Damage"),
            new TableColumn("Cost", ColumnType.WholeNumber),
            new TableColumn("Element", ColumnType.Choice) { Choices = ["Fire", "Ice", "Lightning"] },
            new TableColumn("Roll", ColumnType.Dice),
            new TableColumn("Area", ColumnType.YesNo),
            new TableColumn("Note", ColumnType.Text),
            new TableColumn("Upgrade", ColumnType.Row) { Table = "Spells" },
            new TableColumn("Icon", ColumnType.Sprite),
        ],
        [
            ("Fireball", [TableValue.Of(12.5), TableValue.Of(3L), TableValue.Of("Fire"), TableValue.Of(Dice.Parse("2d6+3")), TableValue.Of(true), TableValue.Of("Hot, \"very\""), TableValue.Of("Fire Storm"), TableValue.Of("Sprites/fire.png")]),
            ("Frost Bolt", [TableValue.Of(8.0), TableValue.Of(2L), TableValue.Of("Ice")]),
            ("Fire Storm", [TableValue.Of(30.0), TableValue.Of(9L), TableValue.Of("Fire"), TableValue.Empty, TableValue.Of(true)]),
        ]);

    [Theory]
    [InlineData("2d6+3", 5, 15, 10)]
    [InlineData("d20", 1, 20, 10.5)]
    [InlineData("3d6 - 2", 1, 16, 8.5)]
    [InlineData("1d8+1d6", 2, 14, 8)]
    [InlineData("5", 5, 5, 5)]
    [InlineData("D4", 1, 4, 2.5)]
    public void Dice_ReadAsWritten_AndSayWhatToExpect(string text, int min, int max, double average)
    {
        var dice = Dice.Parse(text);

        Assert.Equal(min, dice.Min);
        Assert.Equal(max, dice.Max);
        Assert.Equal(average, dice.Average);
        Rand.Seed(3);
        for (var i = 0; i < 200; i++)
            Assert.InRange(dice.Roll(), min, max);
        Assert.Equal(dice, Dice.Parse(dice.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2d")]
    [InlineData("d")]
    [InlineData("2x6")]
    [InlineData("2d6+")]
    [InlineData("-2d6")]
    [InlineData("fireball")]
    public void Dice_RefuseWhatIsntDice(string text) => Assert.False(Dice.TryParse(text, out _));

    [Fact]
    public void Dice_SpreadIsTheirStandardDeviation()
    {
        Assert.Equal(Math.Sqrt(35.0 / 6), Dice.Parse("2d6").Spread, 6);
        Assert.Equal(0, Dice.Parse("4").Spread);
        Assert.Equal("2d6+3", Dice.Parse("2D6 + 3").ToString());
        Assert.Equal("1d20-1", Dice.Parse("d20-1").ToString());
    }

    [Fact]
    public void Columns_ReadWhatTheyHold_AndSayWhatTheyTakeOtherwise()
    {
        var number = new TableColumn("Damage");
        Assert.True(number.TryRead("1.5", out var value, out _));
        Assert.Equal(1.5, value.AsNumber);
        Assert.False(number.TryRead("lots", out _, out var problem));
        Assert.Equal("Damage takes a number, like 12 or 1.5", problem);
        Assert.True(number.TryRead("  ", out var empty, out _));
        Assert.True(empty.IsEmpty);

        var whole = new TableColumn("Cost", ColumnType.WholeNumber);
        Assert.True(whole.TryRead("3.0", out var three, out _));
        Assert.Equal(TableValueKind.WholeNumber, three.Kind);
        Assert.False(whole.TryRead("3.5", out _, out _));

        var yes = new TableColumn("Area", ColumnType.YesNo);
        Assert.True(yes.TryRead("Yes", out var y, out _) && y.AsYesNo);
        Assert.True(yes.TryRead("false", out var n, out _) && !n.AsYesNo);

        var choice = new TableColumn("Element", ColumnType.Choice) { Choices = ["Fire", "Ice"] };
        Assert.True(choice.TryRead("ice", out var ice, out _));
        Assert.Equal("Ice", ice.ToString());
        Assert.False(choice.TryRead("Mud", out _, out problem));
        Assert.Equal("Element takes one of Fire, Ice", problem);

        Assert.True(TableColumn.TryParseType("int", out var type) && type == ColumnType.WholeNumber);
        Assert.True(TableColumn.TryParseType("Yes/No", out type) && type == ColumnType.YesNo);
        Assert.False(TableColumn.TryParseType("spreadsheet", out _));
    }

    [Fact]
    public void Cells_TurnIntoWhatTheyreUsedAs()
    {
        var fireball = Spells()["Fireball"];

        float damage = fireball["Damage"];
        int cost = fireball["Cost"];
        string element = fireball["Element"];
        bool area = fireball["Area"];
        Dice roll = fireball["Roll"];

        Assert.Equal(12.5f, damage);
        Assert.Equal(3, cost);
        Assert.Equal("Fire", element);
        Assert.True(area);
        Assert.Equal(10, roll.Average);
        // An empty cell is 0, "" or no.
        Assert.Equal(0f, (float)Spells()["Frost Bolt"]["Roll"]);
        Assert.Equal("", (string)Spells()["Frost Bolt"]["Note"]);
        Assert.False(Spells()["Frost Bolt"]["Area"]);
    }

    [Fact]
    public void RowsAndColumns_AreFoundByName_AnyCapitals_AndSayWhatThereIsWhenNot()
    {
        var spells = Spells();

        Assert.Equal("Frost Bolt", spells["frost bolt"].Name);
        Assert.Equal(8, (int)spells["Frost Bolt"]["damage"]);
        var row = Assert.Throws<KeyNotFoundException>(() => spells["Fireballl"]);
        Assert.Equal("The Spells table has no row \"Fireballl\". Its rows: Fireball, Frost Bolt, Fire Storm.", row.Message);
        var column = Assert.Throws<KeyNotFoundException>(() => spells["Fireball"]["Dmg"]);
        Assert.Contains("has no column \"Dmg\"", column.Message);
        Assert.Throws<InvalidDataException>(() => new Table("Bad", [new TableColumn("A"), new TableColumn("a")], []));
        Assert.Throws<InvalidDataException>(() => new Table("Bad", [], [("X", []), ("x", [])]));
    }

    [Fact]
    public void RenamedRowsAndColumns_StillAnswerToTheirOldNames()
    {
        var spells = Spells().RenameRow(0, "Fire Ball").RenameRow(0, "Flame");
        spells = spells.ChangeColumn(0, spells.Columns[0] with { Name = "Power" });

        Assert.Equal("Flame", spells["Fireball"].Name);
        Assert.Equal("Flame", spells["Fire Ball"].Name);
        Assert.Equal(12.5, spells["Flame"]["Damage"].AsNumber);
        Assert.Equal("Flame", spells.RenamedRows["Fireball"]);

        // A new row under an old name is that row's now.
        var again = spells.InsertRow(-1, "Fireball");
        Assert.Equal(3, again["Fireball"].Index);
        Assert.False(again.RenamedRows.ContainsKey("Fireball"));
    }

    [Fact]
    public void Edits_MakeNewTables_LeavingTheOldOnesAlone()
    {
        var spells = Spells();

        var changed = spells.SetCell(1, 0, TableValue.Of(9.0)).InsertRow(1, "Spark").MoveColumn(2, 0).RemoveRows([0]);

        Assert.Equal(8, (int)spells["Frost Bolt"]["Damage"]);
        Assert.Equal(9, (int)changed["Frost Bolt"]["Damage"]);
        Assert.Equal(["Spark", "Frost Bolt", "Fire Storm"], changed.Rows.Select(r => r.Name));
        Assert.Equal("Element", changed.Columns[0].Name);
        Assert.Equal("Ice", changed["Frost Bolt"][0].ToString());
        Assert.True(changed["Spark"]["Damage"].IsEmpty);
    }

    [Fact]
    public void Sorting_GoesByTheColumnsType_EmptyCellsLast()
    {
        var spells = Spells();

        Assert.Equal(["Frost Bolt", "Fireball", "Fire Storm"], spells.SortBy(0).Rows.Select(r => r.Name));
        Assert.Equal(["Fire Storm", "Fireball", "Frost Bolt"], spells.SortBy(0, descending: true).Rows.Select(r => r.Name));
        // Choices in their order, ties keep theirs.
        Assert.Equal(["Fireball", "Fire Storm", "Frost Bolt"], spells.SortBy(2).Rows.Select(r => r.Name));
        // Dice by their average, the empty ones last.
        Assert.Equal(["Fireball", "Frost Bolt", "Fire Storm"], spells.SortBy(3).Rows.Select(r => r.Name));
        Assert.Equal(["Fire Storm", "Fireball", "Frost Bolt"], spells.SortBy(-1).Rows.Select(r => r.Name));
    }

    [Fact]
    public void ChangingAColumnsType_ConvertsItsCells_OrRefusesSayingWhich()
    {
        var spells = Spells();

        var asText = spells.ChangeColumn(0, spells.Columns[0] with { Type = ColumnType.Text });
        Assert.Equal(TableValueKind.Text, asText["Fireball"]["Damage"].Kind);
        var back = asText.ChangeColumn(0, asText.Columns[0] with { Type = ColumnType.Number });
        Assert.Equal(12.5, back["Fireball"]["Damage"].AsNumber);

        var refused = Assert.Throws<InvalidDataException>(() => spells.ChangeColumn(0, spells.Columns[0] with { Type = ColumnType.WholeNumber }));
        Assert.Equal("1 cell of Damage isn't a whole number: Fireball (12.5). Change or clear them first.", refused.Message);
    }

    [Fact]
    public void Files_HaveALinePerColumnAndARowPerLine_AndReadBackTheSame()
    {
        var spells = Spells().RenameRow(1, "Ice Bolt");

        var text = TableFile.Write(spells);

        Assert.Equal(
            """
            {
              "columns": [
                { "name": "Damage", "type": "number" },
                { "name": "Cost", "type": "whole number" },
                { "name": "Element", "type": "choice", "choices": ["Fire", "Ice", "Lightning"] },
                { "name": "Roll", "type": "dice" },
                { "name": "Area", "type": "yes/no" },
                { "name": "Note", "type": "text" },
                { "name": "Upgrade", "type": "row", "table": "Spells" },
                { "name": "Icon", "type": "sprite" }
              ],
              "rows": {
                "Fireball": { "Damage": 12.5, "Cost": 3, "Element": "Fire", "Roll": "2d6+3", "Area": true, "Note": "Hot, \"very\"", "Upgrade": "Fire Storm", "Icon": "Sprites/fire.png" },
                "Ice Bolt": { "Damage": 8, "Cost": 2, "Element": "Ice" },
                "Fire Storm": { "Damage": 30, "Cost": 9, "Element": "Fire", "Area": true }
              },
              "renamedRows": { "Frost Bolt": "Ice Bolt" }
            }

            """.ReplaceLineEndings("\n"), text);
        var read = TableFile.Read(text, "Spells");
        Assert.Equal(text, TableFile.Write(read));
        Assert.Equal("Ice Bolt", read["Frost Bolt"].Name);
    }

    [Fact]
    public void AChangedCell_ChangesOneLineOfTheFile()
    {
        var before = TableFile.Write(Spells()).Split('\n');
        var after = TableFile.Write(Spells().SetCell(1, 1, TableValue.Of(4L))).Split('\n');

        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), p => p.First != p.Second);
    }

    [Fact]
    public void Files_KeepWhatTheyDontKnow_AndReadWhatAgentsWrite()
    {
        var text = """
            {
              "columns": [ { "name": "HP", "type": "int", "formula": "=Level * 10" }, "Speed", { "name": "Boss", "type": "bool" } ],
              "rows": [ { "name": "Slime", "HP": "12", "Speed": 1.5, "Boss": "no" }, { "name": "Dragon", "HP": 300, "Boss": true } ],
              "curves": { "xp": [1, 2, 3] },
            }
            """;

        var table = TableFile.Read(text, "Enemies");

        Assert.Equal(ColumnType.WholeNumber, table.Columns[0].Type);
        Assert.Equal(ColumnType.Number, table.Columns[1].Type);
        Assert.Equal(12, (int)table["Slime"]["HP"]);
        Assert.True(table["Dragon"]["Boss"]);
        var written = TableFile.Write(table);
        Assert.Contains("{ \"name\": \"HP\", \"type\": \"whole number\", \"formula\": \"=Level * 10\" }", written);
        Assert.Contains("\"curves\": {\"xp\":[1,2,3]}", written);
        Assert.Equal(written, TableFile.Write(TableFile.Read(written, "Enemies")));
    }

    [Theory]
    [InlineData("""{ "columns": [{ "name": "HP", "type": "whole number" }], "rows": { "Slime": { "HP": "lots" } } }""", "The Enemies table's Slime.HP is \"lots\", but HP takes a whole number, like 3 or 120.")]
    [InlineData("""{ "columns": [{ "name": "HP" }], "rows": { "Slime": { "Hp2": 3 } } }""", "The Enemies table's row \"Slime\" has a value for \"Hp2\", which isn't one of its columns (HP).")]
    [InlineData("""{ "columns": [{ "name": "HP", "type": "huge" }] }""", "The Enemies table's column HP has the type \"huge\"; the types are number, whole number, text, yes/no, choice, dice, sprite, sound, prefab, row.")]
    [InlineData("""{ "columns": [ """, "The Enemies table isn't readable JSON")]
    public void FilesThatDontRead_SayWhereAndWhy(string text, string message)
    {
        var problem = Assert.Throws<InvalidDataException>(() => TableFile.Read(text, "Enemies"));
        Assert.StartsWith(message, problem.Message);
    }

    private static string[] Line(params string[] cells) => cells;

    [Fact]
    public void SpreadsheetText_GoesBothWays_QuotingWhatNeedsIt()
    {
        var rows = new List<IReadOnlyList<string>> { Line("Fireball", "12", "Hot\tand \"bright\""), Line("Multi", "line\ncell", "") };

        var text = TableText.Format(rows, '\t');

        Assert.Equal("Fireball\t12\t\"Hot\tand \"\"bright\"\"\"\r\nMulti\t\"line\ncell\"\t\r\n", text);
        Assert.Equal(rows.Select(r => r.ToList()), TableText.Parse(text, '\t'));
        // As Excel puts one cell on the clipboard, and Google Sheets a row without a last line break.
        Assert.Equal([["12"]], TableText.Parse("12\r\n", '\t'));
        Assert.Equal([["a", "b"]], TableText.Parse("a\tb", '\t'));
    }

    [Fact]
    public void Csv_ExportsAndImports_GuessingTypesAndKeepingKnownOnes()
    {
        var csv = TableText.ToCsv(Spells());
        Assert.StartsWith("Name,Damage,Cost,Element,Roll,Area,Note,Upgrade,Icon\r\nFireball,12.5,3,Fire,2d6+3,yes,\"Hot, \"\"very\"\"\",Fire Storm,Sprites/fire.png\r\n", csv);

        var back = TableText.FromCsv(csv, "Spells", Spells());
        Assert.Equal(TableFile.Write(Spells()), TableFile.Write(back));

        var fresh = TableText.FromCsv("Monster;HP;Speed;Boss;Hit\nSlime;12;1.5;no;1d4\nSlime;20;2;yes;2d6\n;3;;;\n", "Monsters");
        Assert.Equal([ColumnType.WholeNumber, ColumnType.Number, ColumnType.YesNo, ColumnType.Dice], fresh.Columns.Select(c => c.Type));
        Assert.Equal(["Slime", "Slime 2", "Row 3"], fresh.Rows.Select(r => r.Name));
        Assert.Equal(ColumnType.Text, TableText.Guess(["Fire", "Ice"]));
    }
}
