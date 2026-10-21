namespace InboxPilot.Mcp.Mail;

/// <summary>
/// One mailbox. Deliberately has no Send, Forward or Delete:
/// the agent can only read, label and create drafts you review yourself.
/// </summary>
public interface IMailProvider
{
    /// <summary>"gmail" or "outlook".</summary>
    string Account { get; }

    bool IsEnabled { get; }

    /// <summary>Inbox emails received since <paramref name="since"/> that are not yet labelled Processed.</summary>
    Task<IReadOnlyList<MailSummary>> ListUnprocessedAsync(DateTimeOffset since, int max, CancellationToken ct);

    /// <summary>
    /// Emails received since <paramref name="since"/> whose sender matches <paramref name="sender"/>
    /// (full address, or a domain such as "thehousing.expert"), processed or not. Read-only.
    /// </summary>
    Task<IReadOnlyList<MailSummary>> FindFromSenderAsync(string sender, DateTimeOffset since, int max, CancellationToken ct);

    Task<MailDetail> GetAsync(string id, CancellationToken ct);

    /// <summary>Gmail label or Outlook category, full name (e.g. "InboxPilot/P1").</summary>
    Task ApplyLabelAsync(string id, string fullLabelName, CancellationToken ct);

    /// <summary>Creates a native reply draft in the same thread. Returns the draft id.</summary>
    Task<string> CreateReplyDraftAsync(string id, string body, CancellationToken ct);

    /// <summary>Interactive browser sign-in. Only called from the "auth" CLI command.</summary>
    Task AuthenticateInteractiveAsync(CancellationToken ct);
}
