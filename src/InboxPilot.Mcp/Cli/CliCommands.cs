using InboxPilot.Mcp.Configuration;
using InboxPilot.Mcp.Mail;
using InboxPilot.Mcp.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Cli;

/// <summary>
/// Interactive commands you run yourself in a terminal (never from Claude Code):
///   auth [gmail|outlook|all]   browser sign-in, stores refresh tokens
///   test                        lists 3 recent unprocessed emails per mailbox
///   test-notify                 sends a test alert to your phone
///   check-sender <address|domain> [--since-hours 24] [--account outlook|gmail|all] [--notify true] [--only-new true]
///                               <address|domain> may be a comma-separated list. --only-new remembers alerted emails
///                               (first run only saves a baseline), for use from a scheduler.
///                               exit code 0 = found, 10 = none. --notify alerts you (daily cap + dedupe apply)
///   where                       prints the data directory
///   params                      lists every server parameter (switch + env var)
///   config                      prints the effective configuration, secrets masked
/// Every command accepts the same --switch parameters as the MCP server.
/// </summary>
public static class CliCommands
{
    public static readonly string[] Names = ["auth", "test", "test-notify", "check-sender", "where", "params", "config"];

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && Names.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider sp, string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;
        var command = args[0].ToLowerInvariant();
        var target = Positional(args)?.ToLowerInvariant() ?? "all";
        var options = sp.GetRequiredService<IOptions<InboxPilotOptions>>().Value;

        try
        {
            switch (command)
            {
                case "where":
                    Console.WriteLine(options.ResolveDataDirectory());
                    return 0;

                case "params":
                    foreach (var p in InboxPilotParameters.All)
                        Console.WriteLine($"{p.Switch,-30} {p.EnvironmentVariable,-44} {p.Description}");
                    return 0;

                case "config":
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    foreach (var p in InboxPilotParameters.All)
                    {
                        var value = configuration[p.ConfigKey];
                        Console.WriteLine($"{p.Switch,-30} {(p.Secret ? InboxPilotParameters.Mask(value) : value ?? "(default)")}");
                    }
                    return 0;

                case "auth":
                    foreach (var p in Providers(sp, target))
                    {
                        Console.WriteLine($"Signing in to {p.Account}: a browser window will open...");
                        await p.AuthenticateInteractiveAsync(ct);
                        Console.WriteLine($"  {p.Account}: OK");
                    }
                    return 0;

                case "test":
                    foreach (var p in Providers(sp, target))
                    {
                        var emails = await p.ListUnprocessedAsync(DateTimeOffset.UtcNow.AddDays(-2), 3, ct);
                        Console.WriteLine($"{p.Account}: {emails.Count} unprocessed (showing up to 3)");
                        foreach (var e in emails)
                            Console.WriteLine($"  {e.ReceivedAt:yyyy-MM-dd HH:mm}  {e.From}  |  {e.Subject}");
                    }
                    return 0;

                case "check-sender":
                    return await CheckSenderAsync(sp, args, options, ct);

                case "test-notify":
                    var notifier = sp.GetRequiredService<IOwnerNotifier>();
                    await notifier.SendAsync("InboxPilot test message. Setup works.", ct);
                    Console.WriteLine($"Sent via {notifier.Channel}.");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        return 2;
    }

    private static async Task<int> CheckSenderAsync(
        IServiceProvider sp, string[] args, InboxPilotOptions options, CancellationToken ct)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var senders = (Positional(args) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SenderFilter.Normalize).Distinct().ToList();
        if (senders.Count == 0) throw new ArgumentException("Sender is empty.");

        var hours = int.TryParse(configuration["since-hours"], out var h) ? Math.Clamp(h, 1, 24 * 30) : 24;
        var account = (configuration["account"] ?? "all").ToLowerInvariant();
        var notify = bool.TryParse(configuration["notify"], out var n) && n;
        var onlyNew = bool.TryParse(configuration["only-new"], out var o) && o;
        var since = DateTimeOffset.UtcNow.AddHours(-hours);
        var seen = sp.GetRequiredService<SeenStore>();
        var firstRun = onlyNew && !seen.Exists;

        var found = new List<MailSummary>();
        foreach (var p in Providers(sp, account))
        {
            foreach (var sender in senders)
            {
                try
                {
                    found.AddRange(await p.FindFromSenderAsync(sender, since, 20, ct));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{p.Account} ({sender}): {ex.Message}");
                }
            }
        }

        found = found.DistinctBy(e => (e.Account, e.Id)).OrderByDescending(e => e.ReceivedAt).ToList();
        var label = string.Join(", ", senders);

        if (onlyNew)
        {
            var fresh = found.Where(e => !seen.Contains(e.Account, e.Id)).ToList();

            if (firstRun)
            {
                // Baseline: remember what is already there, alert only on emails that arrive from now on.
                foreach (var e in found) seen.Add(e.Account, e.Id);
                seen.Save();
                Console.WriteLine($"Watching '{label}': baseline of {found.Count} existing email(s) saved, no alert sent.");
                return 0;
            }

            if (fresh.Count == 0)
            {
                Console.WriteLine($"No new email from '{label}'.");
                return 10;
            }

            found = fresh;
        }

        if (found.Count == 0)
        {
            Console.WriteLine($"No email from '{label}' in the last {hours}h.");
            return 10;
        }

        Console.WriteLine($"{found.Count} {(onlyNew ? "new " : "")}email(s) from '{label}':");
        foreach (var e in found)
            Console.WriteLine($"  [{e.Account}] {e.ReceivedAt:yyyy-MM-dd HH:mm}  {e.From}  |  {e.Subject}");

        if (notify)
        {
            var guard = sp.GetRequiredService<NotificationGuard>();
            var notifier = sp.GetRequiredService<IOwnerNotifier>();
            // Oldest first, one alert per email. Subject only: nothing from the body is forwarded.
            foreach (var e in found.OrderBy(e => e.ReceivedAt))
            {
                var text = $"Email from {SenderName(e.From)}: {e.Subject} ({e.ReceivedAt:dd/MM HH:mm})";
                var (allowed, reason) = guard.TryReserve(text);
                if (!allowed)
                {
                    Console.WriteLine($"Alert skipped: {reason}");
                    continue; // not marked as seen, retried on the next run
                }

                await notifier.SendAsync(text, ct);
                Console.WriteLine($"Alert sent via {notifier.Channel}.");
                if (onlyNew) { seen.Add(e.Account, e.Id); seen.Save(); }
            }
        }
        else if (onlyNew)
        {
            foreach (var e in found) seen.Add(e.Account, e.Id);
            seen.Save();
        }

        return 0;
    }

    private static string SenderName(string from)
    {
        var i = from.IndexOf('<');
        var name = (i > 0 ? from[..i] : from).Trim().Trim('"');
        return name.Length > 0 ? name : from;
    }

    /// <summary>First positional argument after the command, skipping --switch [value] pairs.</summary>
    private static string? Positional(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (!args[i].Contains('=')) i++; // skip the switch's value
                continue;
            }
            return args[i];
        }
        return null;
    }

    private static IEnumerable<IMailProvider> Providers(IServiceProvider sp, string target)
    {
        var registry = sp.GetRequiredService<MailProviderRegistry>();
        return target == "all" ? registry.Enabled : [registry.Get(target)];
    }
}
