using Azure.Core;
using Azure.Identity;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Me.Messages.Item.CreateReply;
using Microsoft.Graph.Models;

namespace InboxPilot.Mcp.Mail;

/// <summary>Personal Outlook.com / Hotmail mailbox through Microsoft Graph (delegated auth).</summary>
public sealed class OutlookProvider(IOptions<InboxPilotOptions> options, ILogger<OutlookProvider> logger)
    : IMailProvider, IDisposable
{
    // offline_access is added automatically by MSAL; do not request it explicitly.
    private static readonly string[] Scopes =
    [
        "https://graph.microsoft.com/Mail.ReadWrite",
        "https://graph.microsoft.com/User.Read"
    ];

    private readonly InboxPilotOptions _o = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GraphServiceClient? _client;

    public string Account => "outlook";
    public bool IsEnabled => _o.Outlook.Enabled;

    private string AuthRecordPath => Path.Combine(_o.ResolveDataDirectory(), "outlook.auth-record.json");

    public async Task AuthenticateInteractiveAsync(CancellationToken ct)
    {
        EnsureConfigured();
        var credential = CreateCredential(record: null, allowInteractive: true);
        var record = await credential.AuthenticateAsync(new TokenRequestContext(Scopes), ct);

        await using (var fs = File.Create(AuthRecordPath))
            await record.SerializeAsync(fs, ct);

        logger.LogInformation("Outlook authorized for {User}", record.Username);
    }

    public async Task<IReadOnlyList<MailSummary>> ListUnprocessedAsync(DateTimeOffset since, int max, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var processed = _o.Labels.Processed;

        // Graph rule: a property used in $orderby must appear first in $filter.
        var page = await client.Me.MailFolders["inbox"].Messages.GetAsync(cfg =>
        {
            cfg.QueryParameters.Filter = $"receivedDateTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
            cfg.QueryParameters.Orderby = ["receivedDateTime desc"];
            cfg.QueryParameters.Top = Math.Clamp(max * 3, 1, 100);
            cfg.QueryParameters.Select =
                ["id", "conversationId", "from", "subject", "bodyPreview", "receivedDateTime", "categories"];
        }, ct);

        return (page?.Value ?? new List<Message>())
            .Where(m => m.Categories is null || !m.Categories.Contains(processed, StringComparer.OrdinalIgnoreCase))
            .Take(max)
            .Select(m => new MailSummary(
                Account,
                m.Id ?? "",
                m.ConversationId ?? "",
                FormatAddress(m.From?.EmailAddress),
                m.Subject ?? "",
                MailText.Normalize(m.BodyPreview ?? ""),
                m.ReceivedDateTime,
                m.Categories?.ToList() ?? []))
            .ToList();
    }

    public async Task<MailDetail> GetAsync(string id, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var m = await client.Me.Messages[id].GetAsync(cfg =>
        {
            cfg.QueryParameters.Select =
                ["id", "conversationId", "from", "toRecipients", "subject", "receivedDateTime", "body", "hasAttachments"];
            cfg.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        }, ct) ?? throw new InvalidOperationException($"Outlook message '{id}' not found.");

        var rawBody = m.Body?.Content ?? "";
        var body = m.Body?.ContentType == BodyType.Html ? MailText.HtmlToText(rawBody) : MailText.Normalize(rawBody);
        var (text, truncated) = MailText.Truncate(body, _o.MaxBodyChars);

        IReadOnlyList<string> attachments = [];
        if (m.HasAttachments == true)
        {
            var att = await client.Me.Messages[id].Attachments.GetAsync(
                cfg => cfg.QueryParameters.Select = ["name"], ct);
            attachments = att?.Value?.Select(a => a.Name ?? "").Where(n => n.Length > 0).ToList() ?? [];
        }

        return new MailDetail(
            Account,
            m.Id ?? id,
            m.ConversationId ?? "",
            FormatAddress(m.From?.EmailAddress),
            m.ToRecipients?.Select(r => FormatAddress(r.EmailAddress)).ToList() ?? [],
            m.Subject ?? "",
            m.ReceivedDateTime,
            text,
            truncated,
            attachments);
    }

    public async Task ApplyLabelAsync(string id, string fullLabelName, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var current = await client.Me.Messages[id].GetAsync(cfg => cfg.QueryParameters.Select = ["categories"], ct);
        var categories = current?.Categories ?? new List<string>();
        if (categories.Contains(fullLabelName, StringComparer.OrdinalIgnoreCase)) return;

        categories.Add(fullLabelName);
        await client.Me.Messages[id].PatchAsync(new Message { Categories = categories }, cancellationToken: ct);
    }

    public async Task<string> CreateReplyDraftAsync(string id, string body, CancellationToken ct)
    {
        var client = await ClientAsync(ct);

        // createReply puts "comment" above the quoted original and saves it in Drafts. It does NOT send.
        var draft = await client.Me.Messages[id].CreateReply.PostAsync(
            new CreateReplyPostRequestBody { Comment = body }, cancellationToken: ct);

        return draft?.Id ?? throw new InvalidOperationException("Graph returned no draft.");
    }

    // ---------- auth / client ----------

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_o.Outlook.ClientId))
            throw new InvalidOperationException("InboxPilot:Outlook:ClientId is empty. See README, step 'Outlook'.");
    }

    private InteractiveBrowserCredential CreateCredential(AuthenticationRecord? record, bool allowInteractive) =>
        new(new InteractiveBrowserCredentialOptions
        {
            ClientId = _o.Outlook.ClientId,
            TenantId = _o.Outlook.TenantId,
            RedirectUri = new Uri("http://localhost"),
            AuthenticationRecord = record,
            // Headless MCP runs must fail fast instead of opening a browser.
            DisableAutomaticAuthentication = !allowInteractive,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "InboxPilot.Outlook",
                UnsafeAllowUnencryptedStorage = _o.Outlook.AllowUnencryptedTokenCache
            }
        });

    private async Task<GraphServiceClient> ClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;

        await _gate.WaitAsync(ct);
        try
        {
            if (_client is not null) return _client;
            EnsureConfigured();

            if (!File.Exists(AuthRecordPath))
                throw new InvalidOperationException("Outlook is not authorized yet. Run: InboxPilot.Mcp auth outlook");

            AuthenticationRecord record;
            await using (var fs = File.OpenRead(AuthRecordPath))
                record = await AuthenticationRecord.DeserializeAsync(fs, ct);

            var credential = new NonCaeCredential(CreateCredential(record, allowInteractive: false));

            // Fail early with a clear message instead of on the first Graph call.
            try
            {
                await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
            }
            catch (AuthenticationRequiredException ex)
            {
                throw new InvalidOperationException(
                    "Outlook sign-in expired or the token cache is unreadable (macOS: check Keychain access for 'InboxPilot'). " +
                    "Run: InboxPilot.Mcp auth outlook. Detail: " + ex.Message);
            }

            _client = new GraphServiceClient(credential, Scopes);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The Graph SDK (Kiota) requests tokens with Continuous Access Evaluation enabled, but the interactive
    /// sign-in stores its token in the non-CAE cache. Azure.Identity keeps those caches apart, so the silent
    /// lookup misses and asks for interactive sign-in. Forcing one mode for every request keeps them aligned.
    /// </summary>
    internal sealed class NonCaeCredential(TokenCredential inner) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken) =>
            inner.GetToken(WithoutCae(context), cancellationToken);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken cancellationToken) =>
            inner.GetTokenAsync(WithoutCae(context), cancellationToken);

        internal static TokenRequestContext WithoutCae(TokenRequestContext c) =>
            new(c.Scopes, c.ParentRequestId, c.Claims, c.TenantId, isCaeEnabled: false);
    }

    private static string FormatAddress(EmailAddress? a) =>
        a is null ? "" : string.IsNullOrWhiteSpace(a.Name) ? a.Address ?? "" : $"{a.Name} <{a.Address}>";

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }
}
