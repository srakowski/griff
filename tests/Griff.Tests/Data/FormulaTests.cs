using Griff.Data;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Data;

/// <summary>
/// Formula columns (worked out from the row and other tables, the same everywhere, problems said and never thrown),
/// curves that generate level-progression columns, and both in files, games and while a game runs.
/// </summary>
public sealed class FormulaTests
{
    private static TableColumn Formula(string name, string formula, ColumnType type = ColumnType.Number) => new(name, type) { Formula = Griff.Data.Formula.Clean(formula) };

    private static Table Levels(string xp = "=Base * 1.15 ^ (Level - 1)") => new("Levels",
        [new TableColumn("Level", ColumnType.WholeNumber), new TableColumn("Base"), Formula("XP", xp, ColumnType.WholeNumber), Formula("Total", "=Prev(Total) + XP", ColumnType.WholeNumber)],
        [.. Enumerable.Range(1, 5).Select(l => (l.ToString(System.Globalization.CultureInfo.InvariantCulture), (IReadOnlyList<TableValue>)[TableValue.Of((long)l), TableValue.Of(100.0)]))]);

    private static Table Classes() => new("Classes",
        [new TableColumn("Health"), new TableColumn("Speed"), new TableColumn("Role", ColumnType.Choice) { Choices = ["Tank", "Scout"] }],
        [
            ("Warrior", [TableValue.Of(30.0), TableValue.Of(2.0), TableValue.Of("Tank")]),
            ("Rogue", [TableValue.Of(18.0), TableValue.Of(5.0), TableValue.Of("Scout")]),
            ("Dark Knight", [TableValue.Of(40.0), TableValue.Of(1.0), TableValue.Of("Tank")]),
        ]);

    private static TableEvaluation Evaluate(params Table[] tables) => TableEvaluation.Evaluate(tables);

    private static TableValue Cell(TableEvaluation evaluation, string table, string row, string column) => evaluation.Find(table)![row][column];

    [Fact]
    public void AFormulaColumn_IsWorkedOutFromTheRow_TopToBottom()
    {
        var evaluation = Evaluate(Levels());

        Assert.Empty(evaluation.Problems);
        Assert.Equal([100L, 115, 132, 152, 175], evaluation.Find("Levels")!.Rows.Select(r => r["XP"].AsWholeNumber));
        Assert.Equal([100L, 215, 347, 499, 674], evaluation.Find("Levels")!.Rows.Select(r => r["Total"].AsWholeNumber));
        Assert.Equal(TableValueKind.WholeNumber, Cell(evaluation, "Levels", "3", "XP").Kind);
        Assert.True(evaluation.Find("Levels")!.IsComputed);
        // The table as its file has it keeps no values in its formula columns.
        Assert.True(Levels().Rows.All(r => r["XP"].IsEmpty));
    }

    [Theory]
    [InlineData("=2 + 3 * 4", 14)]
    [InlineData("=(2 + 3) * 4", 20)]
    [InlineData("=2 ^ 3 ^ 2", 512)]
    [InlineData("=-2 ^ 2", -4)]
    [InlineData("=7 % 3", 1)]
    [InlineData("=-7 % 3", 2)]
    [InlineData("=10 / 4", 2.5)]
    [InlineData("=Round(2.5) + Round(1.234, 2) + Round(1234, -2)", 1204.23)]
    [InlineData("=Floor(2.7) + Ceiling(2.1) + Abs(-4)", 9)]
    [InlineData("=Sqrt(16) + Pow(2, 10) + Log(1000) + Log(8, 2) + Ln(Exp(2))", 1036)]
    [InlineData("=Min(4, 2, 9) + Max(4, 2, 9) + Clamp(15, 0, 10) + Lerp(10, 20, 0.25)", 33.5)]
    [InlineData("=If(3 > 2 and not (1 = 2), 1, 0) + If(no or yes, 10)", 11)]
    [InlineData("=If(\"fire\" = \"FIRE\", 1, 0) + If(\"a\" < \"b\", 2, 0) + If(3 <> 3, 100, 0)", 3)]
    [InlineData("=1.5e2 + .5", 150.5)]
    public void Formulas_DoTheirSums(string formula, double expected)
    {
        var table = new Table("Sums", [Formula("Value", formula)], [("Row", [])]);
        var evaluation = Evaluate(table);

        Assert.Empty(evaluation.Problems);
        Assert.Equal(expected, Cell(evaluation, "Sums", "Row", "Value").AsNumber, 9);
    }

    [Fact]
    public void Formulas_ReadOtherTables_ThroughRowColumns_Lookups_AndSums()
    {
        var heroes = new Table("Heroes",
            [
                new TableColumn("Class", ColumnType.Row) { Table = "Classes" },
                new TableColumn("Level", ColumnType.WholeNumber),
                new TableColumn("Max Health"),
                Formula("Health", "=Class.Health + Level * 5"),
                Formula("Speed", "=Lookup(\"Classes\", Class, \"Speed\") + Lookup(Classes.Speed, \"Rogue\")"),
                Formula("Knight", "=Classes.[Dark Knight].Health"),
                Formula("All", "=Sum(Classes.Health) & \"/\" & Count(Classes) & \"/\" & Max(Level) & \"/\" & Average([Max Health])", ColumnType.Text),
                Formula("Tanks", "=CountIf(Classes.Role, \"Tank\") * 1000 + SumIf(Classes.Health, Classes.Role, \"Tank\")"),
                Formula("Label", "=Name & \" the \" & Class.Role", ColumnType.Text),
                Formula("Bonus", "=Row() * 10 + Rows()"),
            ],
            [
                ("Ann", [TableValue.Of("Warrior"), TableValue.Of(3L), TableValue.Of(10.0)]),
                ("Bo", [TableValue.Of("Rogue"), TableValue.Of(7L), TableValue.Of(20.0)]),
            ]);
        var evaluation = Evaluate(heroes, Classes());

        Assert.Empty(evaluation.Problems);
        Assert.Equal(45, Cell(evaluation, "Heroes", "Ann", "Health").AsNumber);
        Assert.Equal(53, Cell(evaluation, "Heroes", "Bo", "Health").AsNumber);
        Assert.Equal(7, Cell(evaluation, "Heroes", "Ann", "Speed").AsNumber);
        Assert.Equal(40, Cell(evaluation, "Heroes", "Ann", "Knight").AsNumber);
        Assert.Equal("88/3/7/15", Cell(evaluation, "Heroes", "Ann", "All").ToString());
        Assert.Equal(2070, Cell(evaluation, "Heroes", "Ann", "Tanks").AsNumber);
        Assert.Equal("Bo the Scout", Cell(evaluation, "Heroes", "Bo", "Label").ToString());
        Assert.Equal(22, Cell(evaluation, "Heroes", "Bo", "Bonus").AsNumber);
    }

    [Fact]
    public void Formulas_ReadOtherTablesFormulas_WhateverOrderTheyComeIn()
    {
        // Abilities (alphabetically first) reads Levels' running total, which is worked out first.
        var abilities = new Table("Abilities", [Formula("Unlock", "=Levels.[4].Total")], [("Dash", [])]);
        var forwards = Evaluate(abilities, Levels());
        var backwards = Evaluate(Levels(), abilities);

        Assert.Equal(499, Cell(forwards, "Abilities", "Dash", "Unlock").AsNumber);
        Assert.Equal(Cell(forwards, "Abilities", "Dash", "Unlock"), Cell(backwards, "Abilities", "Dash", "Unlock"));
    }

    [Fact]
    public void Dice_AreMadeAndAddedTo_ForADiceColumn()
    {
        var table = new Table("Weapons",
            [new TableColumn("Base", ColumnType.Dice), new TableColumn("Level", ColumnType.WholeNumber), Formula("Hit", "=Base + Level", ColumnType.Dice),
             Formula("Grows", "=Dice(Level, 6, 2)", ColumnType.Dice), Formula("Both", "=Base + Dice(1, 4)", ColumnType.Dice), Formula("Typical", "=Base * 2")],
            [("Sword", [TableValue.Of(Dice.Parse("1d8")), TableValue.Of(3L)])]);
        var evaluation = Evaluate(table);

        Assert.Empty(evaluation.Problems);
        Assert.Equal("1d8+3", Cell(evaluation, "Weapons", "Sword", "Hit").ToString());
        Assert.Equal("3d6+2", Cell(evaluation, "Weapons", "Sword", "Grows").ToString());
        Assert.Equal("1d8+1d4", Cell(evaluation, "Weapons", "Sword", "Both").ToString());
        Assert.Equal(9, Cell(evaluation, "Weapons", "Sword", "Typical").AsNumber);
    }

    [Fact]
    public void ALoop_OrABadName_IsAProblem_NeverAnException()
    {
        var table = new Table("Broken",
            [
                new TableColumn("Base"),
                Formula("A", "=B + 1"),
                Formula("B", "=A * 2"),
                Formula("Typo", "=Bse * 2"),
                Formula("Uses", "=Typo + 1"),
                Formula("Divide", "=Base / 0"),
                Formula("Words", "=Name * 2"),
                Formula("Gone", "=Monsters.Slime.Health"),
                Formula("Unreadable", "=Base * (2 +"),
                Formula("Fine", "=Base + 1"),
            ],
            [("One", [TableValue.Of(5.0)]), ("Two", [TableValue.Of(6.0)])]);
        var evaluation = Evaluate(table);

        Assert.Equal(6, Cell(evaluation, "Broken", "One", "Fine").AsNumber);
        Assert.True(Cell(evaluation, "Broken", "One", "A").IsEmpty);
        var a = evaluation.ProblemAt("Broken", 0, 1)!;
        Assert.Equal(FormulaProblemKind.Loop, a.Kind);
        Assert.Equal("#loop", a.Mark);
        Assert.Contains("A of One → B of One → A of One", a.Message);
        Assert.Equal(FormulaProblemKind.Loop, evaluation.ProblemAt("Broken", 0, 2)!.Kind);
        Assert.Equal(FormulaProblemKind.Reference, evaluation.ProblemAt("Broken", 1, 3)!.Kind);
        Assert.StartsWith("There's no column or table called \"Bse\".", evaluation.ProblemAt("Broken", 1, 3)!.Message);
        // What uses a cell with a problem has it too, through that cell.
        var uses = evaluation.ProblemAt("Broken", 0, 4)!;
        Assert.Equal("It reads Typo of One, which can't be worked out: " + evaluation.ProblemAt("Broken", 0, 3)!.Message, uses.Message);
        Assert.Equal(evaluation.ProblemAt("Broken", 0, 3)!.Message, uses.Root);
        Assert.Equal("Typo", uses.Through);
        Assert.Equal(FormulaProblemKind.Reference, uses.Kind);
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("The Broken table's Uses, in 2 rows (One, Two): It reads Typo, which can't be worked out: There's no column or table called \"Bse\".", StringComparison.Ordinal));
        Assert.Equal("It divides by 0.", evaluation.ProblemAt("Broken", 0, 5)!.Message);
        Assert.Contains("\"One\" is text, where a number goes", evaluation.ProblemAt("Broken", 0, 6)!.Message);
        Assert.StartsWith("There's no column or table called \"Monsters\"", evaluation.ProblemAt("Broken", 0, 7)!.Message);
        Assert.StartsWith("It doesn't read: The formula ends too soon", evaluation.ProblemAt("Broken", 0, 8)!.Message);
        Assert.Contains(evaluation.Problems, p => p.Column == "Unreadable" && p.Row is null);

        // A game says each once per column, with the rows it's in.
        Assert.Contains("The Broken table's Typo, in 2 rows (One, Two): There's no column or table called \"Bse\". The Broken table's columns: Base, A, B, Typo, Uses, Divide, Words, Gone, Unreadable, Fine. Those cells are empty until that's put right.", evaluation.Warnings);
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("The Broken table's Unreadable formula: It doesn't read", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluating_TheSameTables_GivesTheSameAnswers_AndProblems_InTheSameOrder()
    {
        var broken = new Table("Zed", [Formula("Loop", "=Loop + 1"), Formula("Reads", "=Levels.[5].XP")], [("Only", [])]);
        var first = Evaluate(Levels(), Classes(), broken);
        var second = Evaluate(broken, Classes(), Levels());

        Assert.Equal(first.Problems, second.Problems);
        Assert.Equal(first.Warnings, second.Warnings);
        Assert.Equal(TableFile.Write(first.Find("Levels")!), TableFile.Write(second.Find("Levels")!));
        Assert.Equal(Cell(first, "Zed", "Only", "Reads"), Cell(second, "Zed", "Only", "Reads"));
        Assert.Equal(175, Cell(first, "Zed", "Only", "Reads").AsNumber);
    }

    [Fact]
    public void PowersAndLogarithms_AreWorkedOutTheSameEverywhere_AndRight()
    {
        // Whole powers are multiplied out; the rest go through Griff's own exp and log rather than the machine's library.
        var square = 1.15;
        var expected = 1.0;
        foreach (var bit in new[] { 1, 0, 0, 1 })
        {
            if (bit == 1)
                expected *= square;
            square *= square;
        }

        Assert.Equal(expected, FormulaMath.Pow(1.15, 9));
        foreach (var (x, y) in new[] { (1.15, 0.5), (2.0, 10.5), (10.0, -2.25), (0.3, 3.7), (12345.678, 0.123) })
            Assert.Equal(Math.Pow(x, y), FormulaMath.Pow(x, y), Math.Abs(Math.Pow(x, y)) * 1e-13);
        foreach (var x in new[] { -20.0, -1, -0.001, 0, 0.5, 1, 3.3, 100, 700 })
            Assert.Equal(Math.Exp(x), FormulaMath.Exp(x), Math.Exp(x) * 1e-14);
        foreach (var x in new[] { 1e-300, 0.001, 0.7, 1, 1.4142, 2, 10, 123456.789, 1e300 })
            Assert.Equal(Math.Log(x), FormulaMath.Log(x), Math.Max(1e-15, Math.Abs(Math.Log(x)) * 1e-14));
        Assert.True(double.IsNaN(FormulaMath.Pow(-2, 0.5)));
        Assert.Equal(1, FormulaMath.Pow(0, 0));
    }

    [Fact]
    public void Formulas_ReadAsWritten_AndSayWhatsWrong()
    {
        Assert.Equal("Base * 2", Griff.Data.Formula.Parse("  = Base * 2 ").Text);
        Assert.False(Griff.Data.Formula.TryParse("=Bse(2)", out _, out var problem));
        Assert.StartsWith("There's no function called Bse (at 1). The functions: If, Round", problem);
        Assert.False(Griff.Data.Formula.TryParse("=2 +", out _, out problem));
        Assert.Equal("The formula ends too soon: something's missing at the end.", problem);
        Assert.False(Griff.Data.Formula.TryParse("=\"open", out _, out problem));
        Assert.Equal("The text starting at 1 has no closing \".", problem);
        Assert.False(Griff.Data.Formula.TryParse("=2 # 3", out _, out problem));
        Assert.Equal("A formula can't have \"#\" in it (at 3).", problem);
        Assert.False(Griff.Data.Formula.TryParse("=", out _, out problem));
        Assert.Equal("The formula is empty: write what to work out, like Base * 2.", problem);
        Assert.True(Griff.Data.Formula.TryParse("=[Max Health] * 2 + Levels.10.XP + \"say \"\"hi\"\"\"", out _, out _));
    }

    [Fact]
    public void RenamingAColumn_RenamesItInTheTablesFormulas()
    {
        var levels = Levels();
        var renamed = levels.ChangeColumn(1, new TableColumn("Base XP"));

        Assert.Equal("[Base XP] * 1.15 ^ (Level - 1)", renamed.Column("XP")!.Formula);
        Assert.Equal("Prev(Total) + XP", renamed.Column("Total")!.Formula);
        Assert.Empty(TableEvaluation.Evaluate([renamed]).Problems);
        Assert.Equal("Lookup(\"Base\", Level) + Base2 + [Base XP]", Griff.Data.Formula.RenameColumn("Lookup(\"Base\", Level) + Base2 + Base", "Base", "Base XP"));
        Assert.Equal("Classes.Base + base", Griff.Data.Formula.RenameColumn("Classes.Base + Base", "Base", "base"));
    }

    [Fact]
    public void TheFile_KeepsTheFormula_AndNotItsValues()
    {
        var text = TableFile.Write(TableEvaluation.Evaluate([Levels()]).Find("Levels")!);

        Assert.Contains("{ \"name\": \"XP\", \"type\": \"whole number\", \"formula\": \"=Base * 1.15 ^ (Level - 1)\" }", text);
        Assert.Contains("\"1\": { \"Level\": 1, \"Base\": 100 }", text);
        var read = TableFile.Read(text.Replace("\"Base\": 100 }", "\"Base\": 100, \"XP\": \"#loop\" }", StringComparison.Ordinal), "Levels");
        Assert.Equal("Base * 1.15 ^ (Level - 1)", read.Column("XP")!.Formula);
        Assert.True(read["1"]["XP"].IsEmpty);
        Assert.Equal(text, TableFile.Write(read));
    }

    [Fact]
    public void Tables_GiveScriptsAndTableValues_TheWorkedOutValues_AndAChangeReachesWhatReadsIt()
    {
        var tables = new TableSet([Levels(), Classes(), new Table("Heroes", [new TableColumn("Class", ColumnType.Row) { Table = "Classes" }, Formula("Health", "=Class.Health * 2")], [("Ann", [TableValue.Of("Warrior")])])]);
        var scene = new Scene();
        var ann = new Entity("Ann");
        ann.AddComponent<Health>();
        var values = ann.AddComponent<TableValues>();
        values.Table = "Heroes";
        values.Sets = ["Health.Max = Health"];
        scene.Add(ann);
        var game = new Game(scene, new InputState()) { Tables = tables };
        var warnings = new List<string>();
        game.Warning += (_, w) => warnings.Add(w);

        Assert.Equal(132, (int)tables.Get("Levels", "3")["XP"]);
        game.Tick(1 / 60.0);
        Assert.Equal(60, ann.GetComponent<Health>()!.Max);

        // A change to Classes reaches Heroes' formula, and so the hero, as the next step begins.
        tables.Stage(Classes().SetCell(0, 0, TableValue.Of(50.0)));
        Assert.Equal(60, (float)tables.Get("Heroes", "Ann")["Health"]);
        game.Tick(1 / 60.0);
        Assert.Equal(100, (float)tables.Get("Heroes", "Ann")["Health"]);
        Assert.Equal(100, ann.GetComponent<Health>()!.Max);
        Assert.Empty(warnings);

        // A formula broken while playing is said once, and the game plays on.
        tables.Stage(new Table("Heroes", [new TableColumn("Class", ColumnType.Row) { Table = "Classes" }, Formula("Health", "=Class.Helth * 2")], [("Ann", [TableValue.Of("Warrior")])]));
        game.Tick(1 / 60.0);
        game.Tick(1 / 60.0);
        Assert.Equal(["The Heroes table's Health of Ann: The Classes table has no column \"Helth\". Its columns: Health, Speed, Role. Its cell is empty until that's put right."], warnings);
        Assert.Equal(warnings, tables.Problems);
    }

    // ----- Curves

    [Theory]
    [InlineData(CurveKind.Linear, 2, 10, 100, 55)]
    [InlineData(CurveKind.Power, 2, 10, 100, 32.5)]
    [InlineData(CurveKind.Exponential, 2, 10, 1000, 100)]
    [InlineData(CurveKind.SCurve, 3, 0, 100, 50)]
    public void Curves_RunFromFirstToLast(CurveKind kind, double shape, double first, double last, double middle)
    {
        var curve = new Curve { Kind = kind, First = first, Last = last, Levels = 3, Shape = shape };

        Assert.Null(curve.Problem);
        Assert.Equal([first, middle, last], curve.Values().Select(v => Math.Round(v, 9)));
    }

    [Fact]
    public void ACurveThroughPoints_PassesThroughThem_WithoutOvershooting()
    {
        var curve = new Curve { Kind = CurveKind.Points, Levels = 10, Points = [new(1, 0), new(4, 100), new(7, 100), new(10, 400)] };
        var values = curve.Values();

        Assert.Equal(0, values[0]);
        Assert.Equal(100, values[3]);
        Assert.Equal(100, values[6], 9);
        Assert.Equal(400, values[9]);
        Assert.All(values.Skip(3).Take(4), v => Assert.Equal(100, v, 9));
        Assert.Equal(values.Order(), values);
        Assert.True(Curve.TryReadPoints("1: 0, 4: 100; 10 400", out var read, out _));
        Assert.Equal("1: 0, 4: 100, 10: 400", Curve.WritePoints(read));
        Assert.False(Curve.TryReadPoints("1: 0, lots", out _, out var problem));
        Assert.Equal("\"lots\" isn't a point: write each as a level and its value, like 10: 2000.", problem);
    }

    [Fact]
    public void ACurve_SaysWhyItCantBeGenerated()
    {
        Assert.Equal("An exponential curve needs its first and last values above 0: each level is a share more than the one before.",
            new Curve { First = 0 }.Problem);
        Assert.Equal("A curve needs at least 2 levels.", new Curve { Levels = 1 }.Problem);
        Assert.Equal("Draw at least 2 points for it to pass through.", new Curve { Kind = CurveKind.Points, Points = [new(1, 5)] }.Problem);
        Assert.Throws<InvalidDataException>(() => new Curve { Levels = 0 }.Values());
    }

    [Fact]
    public void Generating_FillsAColumn_ALevelARow_AndKeepsTheCurve()
    {
        var table = new Table("Levels", [new TableColumn("XP", ColumnType.WholeNumber), new TableColumn("Note", ColumnType.Text)], [("Row 1", [TableValue.Empty, TableValue.Of("start")])]);
        var curve = new Curve { Kind = CurveKind.Exponential, First = 100, Last = 5000, Levels = 6, RoundTo = 10 };
        var generated = table.Generate(0, curve);

        Assert.Equal(["1", "2", "3", "4", "5", "6"], generated.Rows.Select(r => r.Name));
        Assert.Equal([100L, 220, 480, 1050, 2290, 5000], generated.Rows.Select(r => r["XP"].AsWholeNumber));
        Assert.Equal("start", generated["Row 1"]["Note"].ToString());
        Assert.Same(curve, generated.Columns[0].Curve);
        Assert.Equal("A curve fills numbers, and Note holds text: make it a number or whole number column first.",
            Assert.Throws<InvalidDataException>(() => generated.Generate(1, curve)).Message);

        // Its file keeps the curve, so it can be generated again.
        var text = TableFile.Write(generated);
        Assert.Contains("\"curve\": { \"kind\": \"exponential\", \"first\": 100, \"last\": 5000, \"levels\": 6, \"roundTo\": 10 }", text);
        var read = TableFile.Read(text, "Levels");
        Assert.Equal(text, TableFile.Write(read));
        Assert.Equal(curve with { Points = read.Columns[0].Curve!.Points }, read.Columns[0].Curve);

        var drawn = new Curve { Kind = CurveKind.Points, Levels = 3, Points = [new(1, 5), new(3, 50)] };
        var hand = TableFile.Write(generated.Generate(0, drawn));
        Assert.Contains("\"curve\": { \"kind\": \"points\", \"levels\": 3, \"points\": [[1, 5], [3, 50]] }", hand);
        Assert.Equal([new CurvePoint(1, 5), new CurvePoint(3, 50)], TableFile.Read(hand, "Levels").Columns[0].Curve!.Points);
    }

    [Fact]
    public void GeneratingOverAFormula_MakesItAColumnOfValues()
    {
        var generated = Levels().Generate(2, new Curve { Kind = CurveKind.Linear, First = 1, Last = 5, Levels = 5 });

        Assert.Null(generated.Column("XP")!.Formula);
        Assert.Equal([1L, 2, 3, 4, 5], generated.Rows.Select(r => r["XP"].AsWholeNumber));
        Assert.Equal([1L, 3, 6, 10, 15], TableEvaluation.Evaluate([generated]).Find("Levels")!.Rows.Select(r => r["Total"].AsWholeNumber));
    }
}
