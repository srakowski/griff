using System.IO;
using Griff.Editor.Features.Scripts.Language;
using Griff.Scripting;
using Microsoft.CodeAnalysis.Classification;

namespace Griff.Editor.Tests;

public sealed class ScriptLanguageTests
{
    private const string Path = @"C:\Game\Assets\Scripts\Ship.cs";

    private const string Ship = """
        /// <summary>Flies the ship.</summary>
        public class Ship : Behavior
        {
            /// <summary>How fast it flies.</summary>
            public float Speed { get; set; } = 5;

            protected override void Update()
            {
                Transform.Position += new Vector3(Input.Horizontal, 0, 0) * Speed * Time.Delta;
            }
        }
        """;

    private static ScriptLanguage Language(string text = Ship)
    {
        var language = new ScriptLanguage();
        language.Load([new ScriptSource(Path, text)]);
        return language;
    }

    [Fact]
    public async Task Complete_AfterDot_ListsGriffMembers()
    {
        var text = Ship.Replace("Input.Horizontal", "Input.");
        var language = Language(text);
        var position = text.IndexOf("Input.", StringComparison.Ordinal) + "Input.".Length;

        Assert.True(language.ShouldComplete(Path, position, '.'));
        var completions = await language.CompleteAsync(Path, position, '.');

        Assert.NotNull(completions);
        Assert.Contains(completions.Items, i => i.DisplayText == "Horizontal");
        Assert.Contains(completions.Items, i => i.DisplayText == "WasPressed");
    }

    [Fact]
    public async Task Complete_InClassBody_OffersOverrides()
    {
        var text = Ship.Replace("    protected override void Update()", "    override \n\n    protected override void Update()");
        var language = Language(text);
        var position = text.IndexOf("override \n", StringComparison.Ordinal) + "override ".Length;

        var completions = await language.CompleteAsync(Path, position, null);

        Assert.NotNull(completions);
        var start = completions.Items.FirstOrDefault(i => i.DisplayText.StartsWith("Start", StringComparison.Ordinal));
        Assert.True(start is not null, string.Join(", ", completions.Items.Select(i => i.DisplayText)));
        var change = await completions.ChangeAsync(start, null);
        Assert.Contains("override void Start()", change.TextChange.NewText);
    }

    [Fact]
    public async Task Complete_RoslynSnippetsAreTagged()
    {
        var text = Ship.Replace("    protected override void Update()", "    pro\n\n    protected override void Update()");
        var language = Language(text);
        var position = text.IndexOf("pro\n", StringComparison.Ordinal) + 3;

        var completions = await language.CompleteAsync(Path, position, null);

        // The editor swaps Roslyn's prop (an int) for Griff's (a float for the Inspector), going by this tag.
        Assert.NotNull(completions);
        var prop = Assert.Single(completions.Items, i => i.DisplayText == "prop");
        Assert.Contains(Microsoft.CodeAnalysis.Tags.WellKnownTags.Snippet, prop.Tags);
    }

    [Fact]
    public async Task QuickInfo_ShowsSignatureAndDocs()
    {
        var language = Language();
        var position = Ship.IndexOf("Speed * Time", StringComparison.Ordinal) + 1;

        var info = await language.QuickInfoAsync(Path, position);

        Assert.NotNull(info);
        var all = string.Concat(info.Sections.SelectMany(s => s.Parts).Select(p => p.Text));
        Assert.Contains("float Ship.Speed", all);
        Assert.Contains("How fast it flies.", all);
    }

    [Fact]
    public async Task QuickInfo_ShowsGriffDocumentation()
    {
        var language = Language();
        var position = Ship.IndexOf("Delta", StringComparison.Ordinal) + 1;

        var info = await language.QuickInfoAsync(Path, position);

        Assert.NotNull(info);
        Assert.Contains(info.Sections, s => s.Kind == Microsoft.CodeAnalysis.QuickInfo.QuickInfoSectionKinds.DocumentationComments);
    }

    [Fact]
    public async Task SignatureHelp_FindsOverloadAndArgument()
    {
        var language = Language();
        var position = Ship.IndexOf("Input.Horizontal, 0", StringComparison.Ordinal) + "Input.Horizontal, ".Length;

        var help = await language.SignatureHelpAsync(Path, position);

        Assert.NotNull(help);
        Assert.Equal(1, help.Argument);
        var selected = help.Signatures[help.Selected];
        Assert.Equal(3, selected.Parameters.Count);
        Assert.Contains("y", string.Concat(selected.Parameters[1].Select(p => p.Text)));
    }

    [Fact]
    public async Task Diagnose_ReportsErrorsWhereTheyAre()
    {
        var text = Ship.Replace("Time.Delta", "Time.Delt");
        var language = Language(text);

        var problems = await language.DiagnoseAsync(Path);

        var error = Assert.Single(problems, p => p.IsError);
        Assert.StartsWith("CS", error.Id);
        Assert.Equal("Delt", text.Substring(error.Start, error.Length));
        Assert.Equal(9, error.Line);
    }

    [Fact]
    public async Task Diagnose_SeesUnsavedTextOfOtherScripts()
    {
        var language = Language(Ship.Replace("Speed * Time", "Helper.Boost * Time"));
        Assert.Contains(await language.DiagnoseAsync(Path), p => p.IsError);

        language.SetText(@"C:\Game\Assets\Scripts\Helper.cs", "public static class Helper { public static float Boost => 2; }");

        Assert.DoesNotContain(await language.DiagnoseAsync(Path), p => p.IsError);
    }

    [Fact]
    public async Task Classify_TellsPropertiesFromMethods()
    {
        var language = Language();

        var spans = await language.ClassifyAsync(Path);

        string KindAt(string word) => spans.First(s => Ship.Substring(s.Start, s.Length) == word && s.Kind != ClassificationTypeNames.StaticSymbol).Kind;
        Assert.Equal(ClassificationTypeNames.PropertyName, KindAt("Speed"));
        Assert.Equal(ClassificationTypeNames.MethodName, KindAt("Update"));
        Assert.Equal(ClassificationTypeNames.ClassName, KindAt("Ship"));
        Assert.Equal(ClassificationTypeNames.StructName, KindAt("Vector3"));
    }

    [Fact]
    public async Task Rename_ChangesEveryUse()
    {
        var language = Language();
        var position = Ship.IndexOf("Speed {", StringComparison.Ordinal);

        Assert.Equal("Speed", await language.RenameTargetAsync(Path, position));
        var changes = await language.RenameAsync(Path, position, "Velocity");

        var text = Assert.Single(changes).Value;
        Assert.DoesNotContain("Speed", text);
        Assert.Equal(2, text.Split("Velocity").Length - 1);
    }

    [Fact]
    public async Task Definition_And_References_StayInTheScripts()
    {
        var language = Language();
        var use = Ship.IndexOf("Speed * Time", StringComparison.Ordinal);

        var definition = await language.DefinitionAsync(Path, use);
        var references = await language.ReferencesAsync(Path, use, thisFileOnly: true);

        Assert.NotNull(definition);
        Assert.Equal(Ship.IndexOf("Speed {", StringComparison.Ordinal), definition.Start);
        Assert.True(references.Count == 2, string.Join(" / ", references.Select(r => $"{r.Start}:{r.Length}:{r.LineText}"))); // the declaration and the use, not the accessors
        Assert.Null(await language.DefinitionAsync(Path, Ship.IndexOf("Vector3", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Format_LaysOutCode()
    {
        const string messy = "public class A : Behavior {\npublic float X {get;set;}=1;\n}";
        var language = Language(messy);

        var changes = await language.FormatAsync(Path);

        var text = messy;
        foreach (var change in changes)
            text = text[..change.Span.Start] + change.NewText + text[change.Span.End..];
        Assert.Contains("    public float X { get; set; } = 1;", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Outline_And_Breadcrumb_ReadTheStructure()
    {
        var outline = ScriptLanguage.Outline(Ship);
        var breadcrumb = ScriptLanguage.Breadcrumb(Ship, Ship.IndexOf("Time.Delta", StringComparison.Ordinal));

        var ship = Assert.Single(outline);
        Assert.Equal("Ship", ship.Name);
        Assert.Equal("Speed", Assert.Single(ship.Properties).Name);
        Assert.Equal(("Ship", "Update()", (string?)null), breadcrumb);
    }
}
