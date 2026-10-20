using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace InboxPilot.Mcp.Mail;

/// <summary>Turns raw email content into plain text that is safe-ish to show a model.</summary>
public static partial class MailText
{
    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var s = ScriptStyleHead().Replace(html, " ");
        s = HiddenElement().Replace(s, " ");
        s = Comment().Replace(s, " ");
        s = BlockBreak().Replace(s, "\n");
        s = AnyTag().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        return Normalize(s);
    }

    /// <summary>Removes zero-width and bidi-override characters often used to hide injected text.</summary>
    public static string RemoveInvisible(string s) => Invisible().Replace(s, string.Empty);

    public static string Normalize(string s)
    {
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        s = RemoveInvisible(s);
        s = HorizontalSpace().Replace(s, " ");
        s = SpaceAroundNewline().Replace(s, "\n");
        s = ManyNewlines().Replace(s, "\n\n");
        return s.Trim();
    }

    public static (string Text, bool Truncated) Truncate(string s, int maxChars)
    {
        if (maxChars <= 0 || s.Length <= maxChars) return (s, false);
        return (s[..maxChars] + "\n[...truncated]", true);
    }

    public static string DecodeBase64Url(string? data)
    {
        if (string.IsNullOrEmpty(data)) return string.Empty;
        var b64 = data.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }

    public static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleHead();

    [GeneratedRegex(@"<(\w+)\b[^>]*style\s*=\s*[""'][^""']*(display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0)[^""']*[""'][^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenElement();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|tr|h[1-6]|table|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[​-‏‪-‮⁠-⁤﻿]")]
    private static partial Regex Invisible();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();
}
