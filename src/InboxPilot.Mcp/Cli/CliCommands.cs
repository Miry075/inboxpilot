using InboxPilot.Mcp.Configuration;
using InboxPilot.Mcp.Mail;
using InboxPilot.Mcp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Cli;

/// <summary>
/// Interactive commands you run yourself in a terminal (never from Claude Code):
///   auth [gmail|outlook|all]   browser sign-in, stores refresh tokens
///   test                        lists 3 recent unprocessed emails per mailbox
///   test-notify                 sends a test alert to your phone
///   where                       prints the data directory
/// </summary>
public static class CliCommands
{
    public static readonly string[] Names = ["auth", "test", "test-notify", "where"];

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && Names.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider sp, string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;
        var command = args[0].ToLowerInvariant();
        var target = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
        var options = sp.GetRequiredService<IOptions<InboxPilotOptions>>().Value;

        try
        {
            switch (command)
            {
                case "where":
                    Console.WriteLine(options.ResolveDataDirectory());
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

    private static IEnumerable<IMailProvider> Providers(IServiceProvider sp, string target)
    {
        var registry = sp.GetRequiredService<MailProviderRegistry>();
        return target == "all" ? registry.Enabled : [registry.Get(target)];
    }
}
