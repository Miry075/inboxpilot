using System.Text;

namespace InboxPilot.Mcp.Mail;

/// <summary>Builds an RFC 5322 plain-text reply for the Gmail drafts API (base64url "raw").</summary>
public static class ReplyMimeBuilder
{
    public static string BuildRaw(string to, string? originalSubject, string? inReplyTo, string? references, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var subject = ReplySubject(originalSubject);
        var sb = new StringBuilder();
        sb.Append("To: ").Append(SanitizeHeader(to)).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");

        if (!string.IsNullOrWhiteSpace(inReplyTo))
        {
            var id = SanitizeHeader(inReplyTo);
            var refs = string.IsNullOrWhiteSpace(references) ? id : $"{SanitizeHeader(references)} {id}";
            sb.Append("In-Reply-To: ").Append(id).Append("\r\n");
            sb.Append("References: ").Append(refs).Append("\r\n");
        }

        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=\"UTF-8\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("\r\n");
        sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n").Replace("\n", "\r\n")),
            Base64FormattingOptions.InsertLineBreaks));
        sb.Append("\r\n");

        return MailText.EncodeBase64Url(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public static string ReplySubject(string? original)
    {
        var s = SanitizeHeader(original ?? string.Empty).Trim();
        if (s.Length == 0) return "Re:";
        return s.StartsWith("re:", StringComparison.OrdinalIgnoreCase) ? s : "Re: " + s;
    }

    /// <summary>Strips CR/LF to block header injection.</summary>
    public static string SanitizeHeader(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    public static string EncodeHeader(string value) =>
        value.All(c => c < 128)
            ? value
            : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
}
