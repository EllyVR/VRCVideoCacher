using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Controls;

/// <summary>
/// Attached property that renders a small markdown subset into a <see cref="TextBlock"/>'s inlines,
/// so remote text (the MOTD) can carry <b>clickable links</b>:
///
/// <code>&lt;TextBlock controls:MarkdownText.Text="{Binding Motd}" /&gt;</code>
///
/// Parsing — and URL validation — lives in <see cref="MarkdownInlineParser"/>; this only builds the
/// visual tree. Do not also set <c>TextBlock.Text</c> on the same control: <c>Text</c> and
/// <c>Inlines</c> are mutually exclusive.
/// </summary>
public static class MarkdownText
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(MarkdownText));

    public static void SetText(TextBlock target, string? value) => target.SetValue(TextProperty, value);
    public static string? GetText(TextBlock target) => target.GetValue(TextProperty);

    static MarkdownText()
    {
        TextProperty.Changed.AddClassHandler<TextBlock, string?>((textBlock, args) =>
            Render(textBlock, args.NewValue.GetValueOrDefault()));
    }

    private static void Render(TextBlock textBlock, string? markdown)
    {
        var inlines = textBlock.Inlines;
        if (inlines is null)
            return;

        inlines.Clear();

        foreach (var span in MarkdownInlineParser.Parse(markdown))
        {
            if (span.LineBreak)
            {
                inlines.Add(new LineBreak());
                continue;
            }

            inlines.Add(span.Url is null ? BuildRun(span) : BuildLink(textBlock, span));
        }
    }

    private static Run BuildRun(MarkdownSpan span) => new(span.Text)
    {
        FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal,
        FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal,
    };

    /// <summary>
    /// A link is a real <see cref="HyperlinkButton"/> so it gets the themed hover, focus and hand
    /// cursor for free — hosted in an <see cref="InlineUIContainer"/> so it flows with the text.
    ///
    /// <c>NavigateUri</c> is deliberately NOT set: that would open the URL through Avalonia's launcher
    /// and bypass <see cref="OpenUrl.Open"/>, which is what enforces the http/https allowlist.
    /// </summary>
    private static InlineUIContainer BuildLink(TextBlock textBlock, MarkdownSpan span)
    {
        var url = span.Url!;
        var button = new HyperlinkButton
        {
            Content = span.Text,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = span.Bold ? FontWeight.Bold : textBlock.FontWeight,
            FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal,
        };

        // Show where the link actually goes — the text is author-controlled and need not match the URL.
        ToolTip.SetTip(button, url);
        button.Click += (_, _) => OpenUrl.Open(url);
        return new InlineUIContainer(button);
    }
}
