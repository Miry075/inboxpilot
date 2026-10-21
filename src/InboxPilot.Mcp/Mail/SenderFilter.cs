namespace InboxPilot.Mcp.Mail;

public static class SenderFilter
{
    /// <summary>
    /// Accepts an address or domain ("syndic@thehousing.expert", "@thehousing.expert", "thehousing.expert").
    /// Only characters valid in addresses are allowed, so the value is safe in a Gmail query.
    /// </summary>
    public static string Normalize(string? sender)
    {
        var s = (sender ?? "").Trim().Trim('<', '>').ToLowerInvariant();
        if (s.Length is 0 or > 254)
            throw new ArgumentException("Sender is empty or too long.");
        if (!s.All(c => char.IsAsciiLetterOrDigit(c) || c is '@' or '.' or '_' or '+' or '-'))
            throw new ArgumentException("Sender may only contain letters, digits and @ . _ + -");
        return s;
    }

    /// <summary>True when the From header ("Name &lt;a@b.c&gt;" or "a@b.c") matches the normalized sender.</summary>
    public static bool Matches(string fromHeader, string normalizedSender)
    {
        var from = fromHeader.ToLowerInvariant();
        var start = from.LastIndexOf('<');
        var address = start >= 0 ? from[(start + 1)..].TrimEnd('>', ' ') : from.Trim();

        if (normalizedSender.Contains('@') && !normalizedSender.StartsWith('@'))
            return address == normalizedSender;

        var domain = normalizedSender.TrimStart('@');
        return address.EndsWith("@" + domain, StringComparison.Ordinal);
    }
}
