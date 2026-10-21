using Microsoft.Extensions.Configuration;

namespace InboxPilot.Mcp.Configuration;

/// <summary>
/// Every setting can be passed as a server parameter when the MCP server is registered:
///   command line:  --whatsapp-token EAAG...          (or --whatsapp-token=EAAG...)
///   environment:   INBOXPILOT_WHATSAPP_TOKEN=EAAG...
/// Precedence (last wins): appsettings.json, appsettings.Local.json, env vars, short env vars, command line.
/// Prefer environment variables for secrets: command-line arguments are visible in the process list.
/// </summary>
public static class InboxPilotParameters
{
    public sealed record Parameter(string Name, string ConfigKey, bool Secret, string Description)
    {
        public string Switch => "--" + Name;
        public string EnvironmentVariable => "INBOXPILOT_" + Name.Replace('-', '_').ToUpperInvariant();
    }

    private const string Root = InboxPilotOptions.SectionName;

    public static readonly IReadOnlyList<Parameter> All =
    [
        new("data-dir", $"{Root}:DataDirectory", false, "Folder for tokens, state, audit logs"),
        new("max-body-chars", $"{Root}:MaxBodyChars", false, "Email body truncation length"),

        new("gmail-enabled", $"{Root}:Gmail:Enabled", false, "true/false"),
        new("gmail-client-secret", $"{Root}:Gmail:ClientSecretPath", false, "Path to Google OAuth client JSON"),

        new("outlook-enabled", $"{Root}:Outlook:Enabled", false, "true/false"),
        new("outlook-client-id", $"{Root}:Outlook:ClientId", false, "Entra app (client) ID"),
        new("outlook-tenant-id", $"{Root}:Outlook:TenantId", false, "'consumers' for Outlook.com"),
        new("outlook-unencrypted-cache", $"{Root}:Outlook:AllowUnencryptedTokenCache", false, "Linux without libsecret only"),

        new("labels-prefix", $"{Root}:Labels:Prefix", false, "Label/category prefix"),

        new("notify-channel", $"{Root}:Notify:Channel", false, "telegram | whatsapp | imessage"),
        new("notify-daily-cap", $"{Root}:Notify:DailyCap", false, "Max alerts per day"),

        new("telegram-bot-token", $"{Root}:Notify:Telegram:BotToken", true, "From @BotFather"),
        new("telegram-chat-id", $"{Root}:Notify:Telegram:ChatId", false, "Your chat id"),

        new("imessage-recipient", $"{Root}:Notify:IMessage:Recipient", false, "Your phone (+32...) or Apple ID email; macOS only"),

        new("whatsapp-token", $"{Root}:Notify:WhatsApp:AccessToken", true, "Meta system-user token"),
        new("whatsapp-phone-number-id", $"{Root}:Notify:WhatsApp:PhoneNumberId", false, "Sender phone number ID"),
        new("whatsapp-to", $"{Root}:Notify:WhatsApp:To", false, "Your number, e.g. 32470000000"),
        new("whatsapp-template", $"{Root}:Notify:WhatsApp:TemplateName", false, "Approved template name"),
        new("whatsapp-template-language", $"{Root}:Notify:WhatsApp:TemplateLanguage", false, "e.g. en, fr"),
        new("whatsapp-api-version", $"{Root}:Notify:WhatsApp:ApiVersion", false, "e.g. v21.0"),
    ];

    public static IDictionary<string, string> SwitchMappings =>
        All.ToDictionary(p => p.Switch, p => p.ConfigKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads INBOXPILOT_*=value lines from %LOCALAPPDATA%/InboxPilot/inboxpilot.env (if present),
    /// so the CLI commands (auth, test, test-notify) see the same parameters as scheduled runs.
    /// </summary>
    public static IConfigurationBuilder AddInboxPilotEnvFile(this IConfigurationBuilder builder, string? path = null)
    {
        path ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InboxPilot", "inboxpilot.env");
        var values = new Dictionary<string, string?>();
        if (File.Exists(path))
        {
            var byEnv = All.ToDictionary(p => p.EnvironmentVariable, StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith('#') || eq <= 0) continue;
                if (byEnv.TryGetValue(line[..eq].Trim(), out var p)) values[p.ConfigKey] = line[(eq + 1)..].Trim();
            }
        }
        return builder.AddInMemoryCollection(values);
    }

    /// <summary>Maps INBOXPILOT_* environment variables to their configuration keys.</summary>
    public static IConfigurationBuilder AddInboxPilotEnvironmentParameters(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var p in All)
        {
            var value = Environment.GetEnvironmentVariable(p.EnvironmentVariable);
            if (!string.IsNullOrEmpty(value)) values[p.ConfigKey] = value;
        }
        return builder.AddInMemoryCollection(values);
    }

    /// <summary>Adds the --switch parameters. Positional CLI verbs (auth, test...) are filtered out first.</summary>
    public static IConfigurationBuilder AddInboxPilotCommandLineParameters(this IConfigurationBuilder builder, string[] args) =>
        builder.AddCommandLine(SwitchArguments(args), SwitchMappings);

    internal static string[] SwitchArguments(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            result.Add(args[i]);
            if (!args[i].Contains('=') && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                result.Add(args[++i]);
        }
        return result.ToArray();
    }

    public static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value.Length <= 6 ? "******" : value[..4] + "******";
}
