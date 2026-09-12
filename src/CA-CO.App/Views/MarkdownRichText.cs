using CaCo.Application.Notes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace CaCo.App.Views;

/// <summary>Dibuja bloques <see cref="MarkdownLite"/> en un RichTextBlock.</summary>
internal static class MarkdownRichText
{
    /// <summary>Construye el bloque enriquecido (vacío si no hay bloques).</summary>
    public static RichTextBlock Build(IReadOnlyList<MdBlock> blocks)
    {
        var rich = new RichTextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        foreach (var block in blocks)
        {
            var paragraph = new Paragraph();
            if (block.Kind == MdBlockKind.Header)
            {
                paragraph.FontSize = block.Level switch { 1 => 20, 2 => 18, _ => 16 };
                paragraph.FontWeight = FontWeights.SemiBold;
            }

            if (block.Kind == MdBlockKind.Bullet)
            {
                paragraph.Inlines.Add(new Run { Text = "•  " });
            }

            foreach (var span in block.Spans)
            {
                var run = new Run { Text = span.Text };
                if (span.Bold)
                {
                    run.FontWeight = FontWeights.SemiBold;
                }

                if (span.Italic)
                {
                    run.FontStyle = FontStyle.Italic;
                }

                if (span.Code)
                {
                    run.FontFamily = new FontFamily("Consolas");
                }

                paragraph.Inlines.Add(run);
            }

            rich.Blocks.Add(paragraph);
        }

        return rich;
    }
}
