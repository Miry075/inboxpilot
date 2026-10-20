using System.Text.Json;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Auditing;

/// <summary>Append-only JSONL log of every action the agent takes. One file per month.</summary>
public sealed class AuditLog(IOptions<InboxPilotOptions> options, TimeProvider time)
{
    private static readonly Lock FileLock = new();
    private readonly InboxPilotOptions _o = options.Value;

    public void Write(string action, string? account = null, string? messageId = null, string? detail = null, bool ok = true)
    {
        var now = time.GetUtcNow();
        var dir = Path.Combine(_o.ResolveDataDirectory(), "audit");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"audit-{now:yyyy-MM}.jsonl");

        var line = JsonSerializer.Serialize(new
        {
            ts = now,
            action,
            account,
            messageId,
            ok,
            detail = detail is { Length: > 300 } ? detail[..300] : detail
        });

        lock (FileLock) File.AppendAllText(path, line + Environment.NewLine);
    }
}
