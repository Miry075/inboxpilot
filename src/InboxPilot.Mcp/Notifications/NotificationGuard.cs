using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Notifications;

/// <summary>
/// Daily cap and duplicate suppression, persisted to disk because every
/// scheduled run starts a fresh MCP server process.
/// </summary>
public sealed class NotificationGuard(IOptions<InboxPilotOptions> options, TimeProvider time)
{
    private static readonly Lock FileLock = new();
    private readonly InboxPilotOptions _o = options.Value;

    private string StatePath => Path.Combine(_o.ResolveDataDirectory(), "notify-state.json");

    public (bool Allowed, string Reason) TryReserve(string text)
    {
        lock (FileLock)
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var state = Load();
            if (state.Day != today) state = new State { Day = today };

            var hash = Hash(text);
            if (state.Hashes.Contains(hash))
                return (false, "Duplicate: the same notification was already sent today.");

            if (state.Count >= _o.Notify.DailyCap)
                return (false, $"Daily cap of {_o.Notify.DailyCap} notifications reached.");

            state.Count++;
            state.Hashes.Add(hash);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
            return (true, "ok");
        }
    }

    private State Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State()
                : new State();
        }
        catch (JsonException)
        {
            return new State();
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant())))[..16];

    private sealed class State
    {
        public DateOnly Day { get; set; }
        public int Count { get; set; }
        public List<string> Hashes { get; set; } = [];
    }
}
