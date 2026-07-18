using System.Text;

namespace VRCVideoCacher.Utils;

/// <summary>One piece of a parsed MOTD: a run of text, or a line break.</summary>
/// <param name="Text">The literal text to show. Empty for a line break.</param>
/// <param name="Url">Non-null when this run is a link. Always an absolute http/https URL.</param>
public readonly record struct MarkdownSpan(
    string Text,
    bool Bold = false,
    bool Italic = false,
    string? Url = null,
    bool LineBreak = false);

/// <summary>
/// Parses the small markdown subset the MOTD banner supports: <c>[text](url)</c>, bare URLs,
/// <c>**bold**</c>, <c>*italic*</c> and newlines. Anything else — including unterminated markers — is
/// literal text, because the MOTD is written by hand and half-rendered markup is worse than none.
///
/// This deliberately holds no Avalonia types. It is the part with all the edge cases, so keeping it
/// pure means it can be unit-tested without a headless UI harness; <c>Controls.MarkdownText</c> turns
/// the spans into inlines.
///
/// <b>URLs are validated here, not at render time.</b> The MOTD is remote content, so a candidate only
/// becomes a link if it parses as an absolute http/https URI — a <c>javascript:</c> or <c>file:</c> URL
/// never reaches a clickable control at all. <see cref="OpenUrl.Open"/> re-checks on click; both gates
/// are intentional.
/// </summary>
public static class MarkdownInlineParser
{
    // Trailing punctuation almost always belongs to the sentence, not the URL:
    // "see https://x.dev/page." should link the page, not the full stop.
    private const string TrailingPunctuation = ".,;:!?)]}'\"";

    public static IReadOnlyList<MarkdownSpan> Parse(string? text)
    {
        var spans = new List<MarkdownSpan>();
        if (string.IsNullOrEmpty(text))
            return spans;

        var buffer = new StringBuilder();
        var bold = false;
        var italic = false;
        var i = 0;

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            spans.Add(new MarkdownSpan(buffer.ToString(), bold, italic));
            buffer.Clear();
        }

        while (i < text.Length)
        {
            var c = text[i];

            // Escapes: \* \[ \\ emit the next character literally.
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '*' or '[' or '\\')
            {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c is '\n')
            {
                Flush();
                spans.Add(new MarkdownSpan(string.Empty, LineBreak: true));
                i++;
                continue;
            }

            if (c is '\r')
            {
                i++; // \r\n — the \n does the work
                continue;
            }

            if (c == '[' && TryParseLink(text, i, out var linkText, out var linkUrl, out var linkEnd))
            {
                Flush();
                spans.Add(new MarkdownSpan(linkText, bold, italic, linkUrl));
                i = linkEnd;
                continue;
            }

            if (c is 'h' && TryParseBareUrl(text, i, out var bareUrl, out var bareEnd))
            {
                Flush();
                spans.Add(new MarkdownSpan(bareUrl, bold, italic, bareUrl));
                i = bareEnd;
                continue;
            }

            // Emphasis only toggles when there is a closing marker; otherwise a lone "*" is just an
            // asterisk and the rest of the message must not silently turn italic.
            if (c == '*')
            {
                var isBold = i + 1 < text.Length && text[i + 1] == '*';
                var marker = isBold ? "**" : "*";
                var open = isBold ? !bold : !italic;

                if (!open || HasCloser(text, i + marker.Length, marker))
                {
                    Flush();
                    if (isBold) bold = !bold; else italic = !italic;
                    i += marker.Length;
                    continue;
                }
            }

            buffer.Append(c);
            i++;
        }

        Flush();
        return spans;
    }

    /// <summary><c>[text](url)</c>. Fails (so the '[' stays literal) on a malformed or non-web URL.</summary>
    private static bool TryParseLink(string text, int start, out string linkText, out string url, out int end)
    {
        linkText = string.Empty;
        url = string.Empty;
        end = start;

        var close = text.IndexOf(']', start + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
            return false;

        var urlEnd = text.IndexOf(')', close + 2);
        if (urlEnd < 0)
            return false;

        var candidate = text[(close + 2)..urlEnd].Trim();
        if (!IsWebUrl(candidate))
            return false;

        linkText = text[(start + 1)..close];
        if (linkText.Length == 0)
            return false;

        url = candidate;
        end = urlEnd + 1;
        return true;
    }

    /// <summary>A bare http(s):// URL running to the next whitespace, minus trailing punctuation.</summary>
    private static bool TryParseBareUrl(string text, int start, out string url, out int end)
    {
        url = string.Empty;
        end = start;

        if (!text.AsSpan(start).StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.AsSpan(start).StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        var stop = start;
        while (stop < text.Length && !char.IsWhiteSpace(text[stop]))
            stop++;

        var candidate = text[start..stop];
        while (candidate.Length > 0 && TrailingPunctuation.Contains(candidate[^1]))
            candidate = candidate[..^1];

        if (!IsWebUrl(candidate))
            return false;

        url = candidate;
        end = start + candidate.Length;
        return true;
    }

    private static bool IsWebUrl(string candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Is there a matching closing marker later on? A lone "*" must stay literal.</summary>
    private static bool HasCloser(string text, int from, string marker)
    {
        if (marker == "**")
            return text.IndexOf("**", from, StringComparison.Ordinal) >= 0;

        // A single "*" is only closed by a lone "*". Asterisks belonging to a "**" pair do not count,
        // or "2 * 3 is not **bold" would treat the bold marker as the italic closer.
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] != '*')
                continue;

            var runEnd = i;
            while (runEnd + 1 < text.Length && text[runEnd + 1] == '*')
                runEnd++;

            if (runEnd == i)
                return true;

            i = runEnd; // skip the whole run of asterisks
        }

        return false;
    }
}
