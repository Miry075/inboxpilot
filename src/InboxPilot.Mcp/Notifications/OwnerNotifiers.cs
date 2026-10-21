using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Notifications;

/// <summary>Sends a message to the mailbox owner only. Recipient is fixed in config, never chosen by the model.</summary>
public interface IOwnerNotifier
{
    string Channel { get; }
    Task SendAsync(string text, CancellationToken ct);
}

public sealed class TelegramNotifier(HttpClient http, IOptions<InboxPilotOptions> options) : IOwnerNotifier
{
    private readonly TelegramOptions _o = options.Value.Notify.Telegram;

    public string Channel => "telegram";

    public async Task SendAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.BotToken) || string.IsNullOrWhiteSpace(_o.ChatId))
            throw new InvalidOperationException("Telegram BotToken/ChatId not configured.");

        var url = $"https://api.telegram.org/bot{_o.BotToken}/sendMessage";
        using var response = await http.PostAsJsonAsync(url, new
        {
            chat_id = _o.ChatId,
            text = "InboxPilot\n" + text,
            disable_web_page_preview = true
        }, ct);

        await NotifierHttp.EnsureSuccessAsync(response, "Telegram", ct);
    }
}

public sealed class WhatsAppNotifier(HttpClient http, IOptions<InboxPilotOptions> options) : IOwnerNotifier
{
    private readonly WhatsAppOptions _o = options.Value.Notify.WhatsApp;

    public string Channel => "whatsapp";

    public async Task SendAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.AccessToken) || string.IsNullOrWhiteSpace(_o.PhoneNumberId) ||
            string.IsNullOrWhiteSpace(_o.To))
            throw new InvalidOperationException("WhatsApp AccessToken/PhoneNumberId/To not configured.");

        object payload = string.IsNullOrWhiteSpace(_o.TemplateName)
            ? new
            {
                messaging_product = "whatsapp",
                to = _o.To,
                type = "text",
                text = new { body = text }
            }
            : new
            {
                messaging_product = "whatsapp",
                to = _o.To,
                type = "template",
                template = new
                {
                    name = _o.TemplateName,
                    language = new { code = _o.TemplateLanguage },
                    components = new object[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new object[] { new { type = "text", text = ForTemplate(text) } }
                        }
                    }
                }
            };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_o.ApiVersion}/{_o.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.AccessToken);

        using var response = await http.SendAsync(request, ct);
        await NotifierHttp.EnsureSuccessAsync(response, "WhatsApp", ct);
    }

    /// <summary>Template variables may not contain newlines, tabs or 4+ consecutive spaces.</summary>
    internal static string ForTemplate(string text)
    {
        var s = text.Replace("\r\n", " | ").Replace('\n', ' ').Replace('\t', ' ');
        while (s.Contains("    ")) s = s.Replace("    ", " ");
        return s.Length > 1000 ? s[..1000] : s;
    }
}

public sealed class IMessageNotifier(IOptions<InboxPilotOptions> options) : IOwnerNotifier
{
    private readonly IMessageOptions _o = options.Value.Notify.IMessage;

    public string Channel => "imessage";

    // Recipient and text are passed as argv, never interpolated into the script (no AppleScript injection).
    private static readonly string[] Script =
    [
        "on run argv",
        "set theRecipient to item 1 of argv",
        "set theText to item 2 of argv",
        "tell application \"Messages\"",
        "set theService to 1st account whose service type = iMessage",
        "send theText to participant theRecipient of theService",
        "end tell",
        "end run"
    ];

    internal static IReadOnlyList<string> BuildArguments(string recipient, string text)
    {
        var args = new List<string>();
        foreach (var line in Script) { args.Add("-e"); args.Add(line); }
        args.Add(recipient);
        args.Add(text.Length > 500 ? text[..500] : text);
        return args;
    }

    public async Task SendAsync(string text, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS())
            throw new InvalidOperationException("The iMessage channel only works on macOS.");
        if (string.IsNullOrWhiteSpace(_o.Recipient))
            throw new InvalidOperationException("iMessage Recipient not configured (phone with country code, e.g. +32470000000, or Apple ID email).");

        var psi = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in BuildArguments(_o.Recipient.Trim(), "InboxPilot\n" + text)) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start osascript.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "iMessage failed: " + (stderr.Length > 500 ? stderr[..500] : stderr).Trim() +
                    " (First run: allow the app/Terminal to control Messages in System Settings > Privacy & Security > Automation.)");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* already exited */ }
            throw new InvalidOperationException("iMessage send timed out (a permission prompt may be waiting on screen).");
        }
    }
}

internal static class NotifierHttp
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string channel, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"{channel} returned {(int)response.StatusCode}: {(body.Length > 500 ? body[..500] : body)}");
    }
}
