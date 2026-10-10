using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Shows <see cref="DocText"/>: paragraphs of wrapped text, code in the mono font, and references to other components as
/// links that run <see cref="NavigateCommand"/> with the component's type.
/// </summary>
public sealed class DocView : StackPanel
{
    public static readonly DependencyProperty DocProperty = DependencyProperty.Register(
        nameof(Doc), typeof(DocText), typeof(DocView), new PropertyMetadata(null, (d, _) => ((DocView)d).Rebuild()));

    public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
        nameof(NavigateCommand), typeof(ICommand), typeof(DocView), new PropertyMetadata(null, (d, _) => ((DocView)d).Rebuild()));

    public static readonly DependencyProperty ParagraphSpacingProperty = DependencyProperty.Register(
        nameof(ParagraphSpacing), typeof(double), typeof(DocView), new PropertyMetadata(8.0, (d, _) => ((DocView)d).Rebuild()));

    public DocText? Doc
    {
        get => (DocText?)GetValue(DocProperty);
        set => SetValue(DocProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public double ParagraphSpacing
    {
        get => (double)GetValue(ParagraphSpacingProperty);
        set => SetValue(ParagraphSpacingProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Doc is null)
            return;

        var previousWasCode = false;
        foreach (var paragraph in Doc.Paragraphs)
        {
            var isCode = paragraph is [{ Kind: DocRunKind.CodeBlock }];
            var block = new TextBlock
            {
                TextWrapping = isCode ? TextWrapping.NoWrap : TextWrapping.Wrap,
                Margin = new Thickness(0, Children.Count == 0 || (isCode && previousWasCode) ? 0 : ParagraphSpacing, 0, 0),
            };
            if (isCode)
            {
                block.FontFamily = (FontFamily)FindResource("Font.Mono");
                block.SetResourceReference(TextBlock.BackgroundProperty, "Brush.Input.Background");
                block.Padding = new Thickness(8, 1, 8, 1);
            }

            foreach (var run in paragraph)
                block.Inlines.Add(Inline(run));
            Children.Add(block);
            previousWasCode = isCode;
        }
    }

    private Inline Inline(DocRun run)
    {
        switch (run.Kind)
        {
            case DocRunKind.Code or DocRunKind.CodeBlock:
                var code = new Run(run.Text) { FontFamily = (FontFamily)FindResource("Font.Mono") };
                if (run.Kind == DocRunKind.Code)
                    code.SetResourceReference(TextElement.BackgroundProperty, "Brush.Input.Background");
                return code;
            case DocRunKind.Reference when run.Target is { } target && target.IsSubclassOf(typeof(Component)) && NavigateCommand is { } navigate:
                var link = new Hyperlink(new Run(run.Text))
                {
                    Command = navigate,
                    CommandParameter = target,
                    TextDecorations = null,
                    ToolTip = $"Show {target.Name} in the catalog",
                    FontWeight = FontWeights.SemiBold,
                };
                link.SetResourceReference(TextElement.ForegroundProperty, "Brush.Status.Info");
                return link;
            case DocRunKind.Reference:
                return new Run(run.Text) { FontWeight = FontWeights.SemiBold };
            default:
                return new Run(run.Text);
        }
    }
}
