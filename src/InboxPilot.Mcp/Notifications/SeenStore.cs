using System.Text.Json;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Notifications;

/// <summary>Remembers which emails a watcher already alerted on, so repeated polling never re-alerts.</summary>
public sealed class SeenStore(IOptions<InboxPilotOptions> options)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly InboxPilotOptions _o = options.Value;
    private Dictionary<string, DateTimeOffset>? _seen;

    private string Path_ => Path.Combine(_o.ResolveDataDirectory(), "seen-emails.json");

    /// <summary>False on the very first run (no state file yet): the caller should baseline instead of alerting.</summary>
    public bool Exists => File.Exists(Path_);

    public bool Contains(string account, string id) => Load().ContainsKey(Key(account, id));

    public void Add(string account, string id) => Load()[Key(account, id)] = DateTimeOffset.UtcNow;

    public void Save()
    {
        var seen = Load();
        foreach (var k in seen.Where(p => DateTimeOffset.UtcNow - p.Value > Retention).Select(p => p.Key).ToList())
            seen.Remove(k);
        Directory.CreateDirectory(_o.ResolveDataDirectory());
        File.WriteAllText(Path_, JsonSerializer.Serialize(seen));
    }

    private static string Key(string account, string id) => $"{account}:{id}";

    private Dictionary<string, DateTimeOffset> Load()
    {
        if (_seen is not null) return _seen;
        try
        {
            _seen = File.Exists(Path_)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(Path_)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            _seen = new();
        }
        return _seen;
    }
}
