using System.Windows;
using System.Windows.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Tags;

namespace Griff.Editor.Features.Scripts.Editor;

/// <summary>How code looks: "Ember", a warm dark theme built around Griff's orange, with semantic colors from Roslyn.</summary>
public static class CodeTheme
{
    public static readonly FontFamily Font = new(new Uri("pack://application:,,,/Griff.Editor;component/"), "./Themes/Fonts/#JetBrains Mono");

    public const double FontSize = 14;

    public static readonly Brush Background = Frozen(0x13, 0x14, 0x18);
    public static readonly Brush Text = Frozen(0xDC, 0xDF, 0xE6);
    public static readonly Brush Caret = Frozen(0xF0, 0x8C, 0x2E);
    public static readonly Brush CurrentLine = Frozen(0x1B, 0x1D, 0x23);
    public static readonly Brush CurrentLineBorder = Frozen(0x22, 0x24, 0x2B);
    public static readonly Brush Selection = Frozen(0x2B, 0x4A, 0x75, 0xC0);
    public static readonly Brush LineNumber = Frozen(0x45, 0x49, 0x53);
    public static readonly Brush LineNumberCurrent = Frozen(0xC9, 0xCD, 0xD6);
    public static readonly Brush IndentGuide = Frozen(0x25, 0x27, 0x2E);
    public static readonly Brush IndentGuideActive = Frozen(0x3D, 0x41, 0x4C);
    public static readonly Brush BracketMatch = Frozen(0xF0, 0x8C, 0x2E, 0x38);
    public static readonly Brush BracketMatchBorder = Frozen(0xF0, 0x8C, 0x2E, 0x90);
    public static readonly Brush Occurrence = Frozen(0x5E, 0x8B, 0xC7, 0x30);
    public static readonly Brush OccurrenceWrite = Frozen(0xC7, 0x8B, 0x5E, 0x34);
    public static readonly Brush SearchMatch = Frozen(0xE3, 0xB3, 0x41, 0x40);
    public static readonly Brush SearchMatchBorder = Frozen(0xE3, 0xB3, 0x41, 0xB0);
    public static readonly Brush Error = Frozen(0xFF, 0x5F, 0x56);
    public static readonly Brush Warning = Frozen(0xE3, 0xB3, 0x41);
    public static readonly Brush Folding = Frozen(0x5E, 0x62, 0x6E);
    public static readonly Brush FoldingBackground = Frozen(0x13, 0x14, 0x18);
    public static readonly Brush Popup = Frozen(0x1C, 0x1E, 0x24);
    public static readonly Brush PopupBorder = Frozen(0x33, 0x36, 0x3F);
    public static readonly Brush PopupSelection = Frozen(0x2A, 0x3F, 0x5F);
    public static readonly Brush PopupMatch = Frozen(0xF5, 0xA4, 0x53);

    // Syntax
    public static readonly Brush Keyword = Frozen(0xF2, 0x9E, 0x6B);
    public static readonly Brush ControlKeyword = Frozen(0xF0, 0x7E, 0x8C);
    public static readonly Brush Type = Frozen(0x6F, 0xD6, 0xC5);
    public static readonly Brush ValueType = Frozen(0x8F, 0xE3, 0xA8);
    public static readonly Brush Interface = Frozen(0xA8, 0xE6, 0xCF);
    public static readonly Brush TypeParameter = Frozen(0x9D, 0xD8, 0xE8);
    public static readonly Brush Method = Frozen(0x82, 0xB6, 0xFF);
    public static readonly Brush Property = Frozen(0xCF, 0xB8, 0xFF);
    public static readonly Brush Field = Frozen(0xBC, 0xAE, 0xE8);
    public static readonly Brush EventName = Frozen(0xE6, 0xA8, 0xD7);
    public static readonly Brush Parameter = Frozen(0xF3, 0xD9, 0xA4);
    public static readonly Brush Local = Frozen(0xDC, 0xDF, 0xE6);
    public static readonly Brush String = Frozen(0xAE, 0xD9, 0x7F);
    public static readonly Brush StringEscape = Frozen(0xF5, 0xC5, 0x7A);
    public static readonly Brush Number = Frozen(0xF5, 0xC5, 0x7A);
    public static readonly Brush Comment = Frozen(0x67, 0x6E, 0x7D);
    public static readonly Brush DocComment = Frozen(0x5F, 0x8A, 0x70);
    public static readonly Brush DocTag = Frozen(0x4E, 0x72, 0x5D);
    public static readonly Brush Punctuation = Frozen(0x8E, 0x94, 0xA3);
    public static readonly Brush Operator = Frozen(0xB4, 0xBA, 0xC6);
    public static readonly Brush Namespace = Frozen(0xC4, 0xC8, 0xD2);
    public static readonly Brush EnumMember = Frozen(0xF5, 0xC5, 0x7A);
    public static readonly Brush Preprocessor = Frozen(0x9A, 0xA0, 0xAC);
    public static readonly Brush Unnecessary = Frozen(0x6A, 0x70, 0x7C);

    /// <summary>The brush, and whether it's italic, for a Roslyn classification; null for plain text.</summary>
    public static (Brush Brush, bool Italic, bool Bold)? Style(string kind) => kind switch
    {
        ClassificationTypeNames.Keyword => (Keyword, false, false),
        ClassificationTypeNames.ControlKeyword => (ControlKeyword, false, false),
        ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName or ClassificationTypeNames.DelegateName => (Type, false, false),
        ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName or ClassificationTypeNames.EnumName => (ValueType, false, false),
        ClassificationTypeNames.InterfaceName => (Interface, true, false),
        ClassificationTypeNames.TypeParameterName => (TypeParameter, true, false),
        ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => (Method, false, false),
        ClassificationTypeNames.PropertyName => (Property, false, false),
        ClassificationTypeNames.FieldName or ClassificationTypeNames.ConstantName => (Field, false, false),
        ClassificationTypeNames.EventName => (EventName, false, false),
        ClassificationTypeNames.ParameterName => (Parameter, true, false),
        ClassificationTypeNames.LocalName => (Local, false, false),
        ClassificationTypeNames.EnumMemberName => (EnumMember, false, false),
        ClassificationTypeNames.NamespaceName => (Namespace, false, false),
        ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral => (String, false, false),
        ClassificationTypeNames.StringEscapeCharacter => (StringEscape, false, false),
        ClassificationTypeNames.NumericLiteral => (Number, false, false),
        ClassificationTypeNames.Comment => (Comment, true, false),
        ClassificationTypeNames.XmlDocCommentText => (DocComment, true, false),
        ClassificationTypeNames.XmlDocCommentDelimiter or ClassificationTypeNames.XmlDocCommentName or ClassificationTypeNames.XmlDocCommentAttributeName
            or ClassificationTypeNames.XmlDocCommentAttributeQuotes or ClassificationTypeNames.XmlDocCommentAttributeValue
            or ClassificationTypeNames.XmlDocCommentEntityReference or ClassificationTypeNames.XmlDocCommentCDataSection => (DocTag, false, false),
        ClassificationTypeNames.Punctuation => (Punctuation, false, false),
        ClassificationTypeNames.Operator or ClassificationTypeNames.OperatorOverloaded => (Operator, false, false),
        ClassificationTypeNames.PreprocessorKeyword or ClassificationTypeNames.PreprocessorText => (Preprocessor, false, false),
        _ => null,
    };

    /// <summary>The brush for a part of a tooltip or signature (Roslyn's text tags).</summary>
    public static Brush ForTag(string tag) => tag switch
    {
        TextTags.Keyword => Keyword,
        TextTags.Class or TextTags.Record or TextTags.Delegate => Type,
        TextTags.Struct or TextTags.RecordStruct or TextTags.Enum => ValueType,
        TextTags.Interface => Interface,
        TextTags.TypeParameter => TypeParameter,
        TextTags.Method or TextTags.ExtensionMethod => Method,
        TextTags.Property => Property,
        TextTags.Field or TextTags.Constant => Field,
        TextTags.Event => EventName,
        TextTags.Parameter => Parameter,
        TextTags.Local or TextTags.RangeVariable => Local,
        TextTags.EnumMember => EnumMember,
        TextTags.Namespace or TextTags.Module => Namespace,
        TextTags.StringLiteral => String,
        TextTags.NumericLiteral => Number,
        TextTags.Punctuation => Punctuation,
        TextTags.Operator => Operator,
        _ => Text,
    };

    /// <summary>A completion item's badge letter and its color, from Roslyn's item tags (m for a method, p for a property...).</summary>
    public static (string Letter, Brush Brush) Glyph(IEnumerable<string> tags)
    {
        var set = tags.ToHashSet();
        if (set.Contains("Snippet")) return ("{}", Caret);
        if (set.Contains(WellKnownTags.Method) || set.Contains(WellKnownTags.ExtensionMethod)) return ("m", Method);
        if (set.Contains(WellKnownTags.Property)) return ("p", Property);
        if (set.Contains(WellKnownTags.Field) || set.Contains(WellKnownTags.Constant)) return ("f", Field);
        if (set.Contains(WellKnownTags.Event)) return ("e", EventName);
        if (set.Contains(WellKnownTags.Class) || set.Contains(WellKnownTags.Delegate)) return ("C", Type);
        if (set.Contains(WellKnownTags.Structure)) return ("S", ValueType);
        if (set.Contains(WellKnownTags.Interface)) return ("I", Interface);
        if (set.Contains(WellKnownTags.Enum)) return ("E", ValueType);
        if (set.Contains(WellKnownTags.EnumMember)) return ("v", EnumMember);
        if (set.Contains(WellKnownTags.Namespace)) return ("N", Namespace);
        if (set.Contains(WellKnownTags.Keyword)) return ("k", Keyword);
        if (set.Contains(WellKnownTags.TypeParameter)) return ("T", TypeParameter);
        if (set.Contains(WellKnownTags.Local) || set.Contains(WellKnownTags.Parameter) || set.Contains(WellKnownTags.RangeVariable)) return ("x", Parameter);
        return ("·", Text);
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Pen Pen(Brush brush, double thickness = 1)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    public static Typeface Typeface(FontStyle style, FontWeight weight) => new(Font, style, weight, FontStretches.Normal);
}
