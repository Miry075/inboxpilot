namespace InboxPilot.Mcp.Configuration;

public sealed class InboxPilotOptions
{
    public const string SectionName = "InboxPilot";

    /// <summary>Where tokens, state and audit logs live. Empty = %LOCALAPPDATA%/InboxPilot.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>Email bodies are truncated to this many characters before reaching the model.</summary>
    public int MaxBodyChars { get; set; } = 8000;

    public GmailOptions Gmail { get; set; } = new();
    public OutlookOptions Outlook { get; set; } = new();
    public LabelOptions Labels { get; set; } = new();
    public NotifyOptions Notify { get; set; } = new();

    public string ResolveDataDirectory()
    {
        var dir = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InboxPilot")
            : Environment.ExpandEnvironmentVariables(DataDirectory);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Relative paths resolve against the data directory, keeping secrets out of the repo.</summary>
    public string ResolvePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expanded) ? expanded : Path.Combine(ResolveDataDirectory(), expanded);
    }
}

public sealed class GmailOptions
{
    public bool Enabled { get; set; } = true;
    public string ClientSecretPath { get; set; } = "client_secret.json";
}

public sealed class OutlookOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Application (client) ID of your Entra app registration.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>"consumers" for personal Outlook.com / Hotmail / Live accounts.</summary>
    public string TenantId { get; set; } = "consumers";

    /// <summary>Linux without libsecret only. Keep false on Windows/macOS.</summary>
    public bool AllowUnencryptedTokenCache { get; set; }
}

public sealed class LabelOptions
{
    private static readonly string[] Defaults =
        ["P1", "P2", "P3", "P4", "Client", "MissionOffer", "Recruiter", "Admin", "Finance", "Personal", "Wedding", "Newsletter", "Review"];

    public string Prefix { get; set; } = "InboxPilot";

    /// <summary>Whitelist of labels the agent may apply. Anything else is rejected.</summary>
    public string[]? Allowed { get; set; }

    public IReadOnlyList<string> EffectiveAllowed => Allowed is { Length: > 0 } ? Allowed : Defaults;

    public string Processed => $"{Prefix}/Processed";

    public string FullName(string shortName) => $"{Prefix}/{shortName}";
}

public sealed class NotifyOptions
{
    /// <summary>"telegram", "whatsapp" or "imessage" (macOS only).</summary>
    public string Channel { get; set; } = "telegram";

    /// <summary>Hard cap on owner notifications per calendar day.</summary>
    public int DailyCap { get; set; } = 10;

    public TelegramOptions Telegram { get; set; } = new();
    public WhatsAppOptions WhatsApp { get; set; } = new();
    public IMessageOptions IMessage { get; set; } = new();
}

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
}

public sealed class IMessageOptions
{
    /// <summary>Your own phone (international format with "+") or Apple ID email. macOS only.</summary>
    public string Recipient { get; set; } = "";
}

public sealed class WhatsAppOptions
{
    public string AccessToken { get; set; } = "";
    public string PhoneNumberId { get; set; } = "";

    /// <summary>Your own number, international format without "+", e.g. 32470000000.</summary>
    public string To { get; set; } = "";

    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Approved template with one body variable {{1}}. Required for messages outside the
    /// 24h customer-service window. Empty = send free-form text (works only inside the window).
    /// </summary>
    public string TemplateName { get; set; } = "";

    public string TemplateLanguage { get; set; } = "en";
}
