using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.Util.Store;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp.Mail;

public sealed class GmailProvider(IOptions<InboxPilotOptions> options, ILogger<GmailProvider> logger)
    : IMailProvider, IDisposable
{
    private const string Me = "me";
    private const string TokenUser = "user";
    private static readonly string[] Scopes = [GmailService.Scope.GmailModify];

    private readonly InboxPilotOptions _o = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GmailService? _service;
    private Dictionary<string, string>? _idByName;
    private Dictionary<string, string>? _nameById;

    public string Account => "gmail";
    public bool IsEnabled => _o.Gmail.Enabled;

    private string TokenDirectory => Path.Combine(_o.ResolveDataDirectory(), "gmail-token");
    private string ClientSecretPath => _o.ResolvePath(_o.Gmail.ClientSecretPath);

    public async Task AuthenticateInteractiveAsync(CancellationToken ct)
    {
        var credential = await AuthorizeAsync(ct);
        using var svc = CreateService(credential);
        var profile = await svc.Users.GetProfile(Me).ExecuteAsync(ct);
        logger.LogInformation("Gmail authorized for {Email}", profile.EmailAddress);
    }

    public async Task<IReadOnlyList<MailSummary>> ListUnprocessedAsync(DateTimeOffset since, int max, CancellationToken ct)
    {
        var svc = await ServiceAsync(ct);
        var processedId = await GetOrCreateLabelIdAsync(_o.Labels.Processed, ct);

        var list = svc.Users.Messages.List(Me);
        list.Q = $"in:inbox after:{since.ToUnixTimeSeconds()}";
        list.MaxResults = Math.Clamp(max * 3, 1, 100);
        var response = await list.ExecuteAsync(ct);

        var results = new List<MailSummary>();
        foreach (var stub in response.Messages ?? Array.Empty<Message>())
        {
            if (results.Count >= max) break;

            var get = svc.Users.Messages.Get(Me, stub.Id);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            get.MetadataHeaders = new Repeatable<string>(["From", "Subject", "Date"]);
            var msg = await get.ExecuteAsync(ct);

            var labelIds = msg.LabelIds ?? new List<string>();
            if (labelIds.Contains(processedId)) continue;

            results.Add(new MailSummary(
                Account,
                msg.Id,
                msg.ThreadId,
                Header(msg.Payload, "From") ?? "",
                Header(msg.Payload, "Subject") ?? "",
                MailText.Normalize(System.Net.WebUtility.HtmlDecode(msg.Snippet ?? "")),
                ToDate(msg.InternalDate),
                await LabelNamesAsync(labelIds, ct)));
        }

        return results;
    }

    public async Task<IReadOnlyList<MailSummary>> FindFromSenderAsync(
        string sender, DateTimeOffset since, int max, CancellationToken ct)
    {
        var normalized = SenderFilter.Normalize(sender);
        var svc = await ServiceAsync(ct);

        var list = svc.Users.Messages.List(Me);
        list.Q = $"from:{normalized} after:{since.ToUnixTimeSeconds()}";
        list.MaxResults = Math.Clamp(max, 1, 100);
        var response = await list.ExecuteAsync(ct);

        var results = new List<MailSummary>();
        foreach (var stub in response.Messages ?? Array.Empty<Message>())
        {
            var get = svc.Users.Messages.Get(Me, stub.Id);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            get.MetadataHeaders = new Repeatable<string>(["From", "Subject", "Date"]);
            var msg = await get.ExecuteAsync(ct);

            var from = Header(msg.Payload, "From") ?? "";
            if (!SenderFilter.Matches(from, normalized)) continue; // Gmail from: also matches partial names

            results.Add(new MailSummary(
                Account, msg.Id, msg.ThreadId, from,
                Header(msg.Payload, "Subject") ?? "",
                MailText.Normalize(System.Net.WebUtility.HtmlDecode(msg.Snippet ?? "")),
                ToDate(msg.InternalDate),
                await LabelNamesAsync(msg.LabelIds ?? new List<string>(), ct)));
        }

        return results;
    }

    public async Task<MailDetail> GetAsync(string id, CancellationToken ct)
    {
        var svc = await ServiceAsync(ct);
        var get = svc.Users.Messages.Get(Me, id);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var msg = await get.ExecuteAsync(ct);

        var (text, truncated) = MailText.Truncate(ExtractBody(msg.Payload), _o.MaxBodyChars);
        var attachments = new List<string>();
        CollectAttachments(msg.Payload, attachments);

        return new MailDetail(
            Account,
            msg.Id,
            msg.ThreadId,
            Header(msg.Payload, "From") ?? "",
            SplitAddresses(Header(msg.Payload, "To")),
            Header(msg.Payload, "Subject") ?? "",
            ToDate(msg.InternalDate),
            text,
            truncated,
            attachments);
    }

    public async Task ApplyLabelAsync(string id, string fullLabelName, CancellationToken ct)
    {
        var svc = await ServiceAsync(ct);
        var labelId = await GetOrCreateLabelIdAsync(fullLabelName, ct);
        await svc.Users.Messages
            .Modify(new ModifyMessageRequest { AddLabelIds = [labelId] }, Me, id)
            .ExecuteAsync(ct);
    }

    public async Task<string> CreateReplyDraftAsync(string id, string body, CancellationToken ct)
    {
        var svc = await ServiceAsync(ct);
        var get = svc.Users.Messages.Get(Me, id);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        get.MetadataHeaders = new Repeatable<string>(["From", "Reply-To", "Subject", "Message-ID", "References"]);
        var original = await get.ExecuteAsync(ct);

        var to = Header(original.Payload, "Reply-To") ?? Header(original.Payload, "From")
                 ?? throw new InvalidOperationException("Original email has no sender to reply to.");

        var raw = ReplyMimeBuilder.BuildRaw(
            to,
            Header(original.Payload, "Subject"),
            Header(original.Payload, "Message-ID"),
            Header(original.Payload, "References"),
            body);

        var draft = await svc.Users.Drafts
            .Create(new Draft { Message = new Message { Raw = raw, ThreadId = original.ThreadId } }, Me)
            .ExecuteAsync(ct);

        return draft.Id;
    }

    // ---------- auth / service ----------

    private async Task<UserCredential> AuthorizeAsync(CancellationToken ct)
    {
        if (!File.Exists(ClientSecretPath))
            throw new InvalidOperationException(
                $"Gmail OAuth client file not found at '{ClientSecretPath}'. See README, step 'Gmail'.");

        await using var stream = File.OpenRead(ClientSecretPath);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, TokenUser, ct, new FileDataStore(TokenDirectory, fullPath: true));
    }

    private async Task<GmailService> ServiceAsync(CancellationToken ct)
    {
        if (_service is not null) return _service;

        await _gate.WaitAsync(ct);
        try
        {
            if (_service is not null) return _service;

            // Never open a browser from the MCP server: it runs headless under Claude Code.
            if (!Directory.Exists(TokenDirectory) || !Directory.EnumerateFiles(TokenDirectory).Any())
                throw new InvalidOperationException("Gmail is not authorized yet. Run: InboxPilot.Mcp auth gmail");

            _service = CreateService(await AuthorizeAsync(ct));
            return _service;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static GmailService CreateService(UserCredential credential) =>
        new(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "InboxPilot" });

    // ---------- labels ----------

    private async Task LoadLabelsAsync(CancellationToken ct)
    {
        var svc = await ServiceAsync(ct);
        var labels = (await svc.Users.Labels.List(Me).ExecuteAsync(ct)).Labels ?? new List<Label>();
        _idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in labels.Where(l => l.Id is not null && l.Name is not null))
        {
            _idByName[l.Name] = l.Id;
            _nameById[l.Id] = l.Name;
        }
    }

    private async Task<string> GetOrCreateLabelIdAsync(string name, CancellationToken ct)
    {
        if (_idByName is null) await LoadLabelsAsync(ct);
        if (_idByName!.TryGetValue(name, out var existing)) return existing;

        // Gmail shows "A/B" nested under "A" only if "A" exists.
        var slash = name.LastIndexOf('/');
        if (slash > 0) await GetOrCreateLabelIdAsync(name[..slash], ct);

        var svc = await ServiceAsync(ct);
        var created = await svc.Users.Labels.Create(
            new Label { Name = name, LabelListVisibility = "labelShow", MessageListVisibility = "show" }, Me)
            .ExecuteAsync(ct);

        _idByName[name] = created.Id;
        _nameById![created.Id] = name;
        logger.LogInformation("Created Gmail label {Label}", name);
        return created.Id;
    }

    private async Task<IReadOnlyList<string>> LabelNamesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        if (_nameById is null) await LoadLabelsAsync(ct);
        return ids.Select(id => _nameById!.TryGetValue(id, out var n) ? n : id).ToList();
    }

    // ---------- parsing ----------

    private static string? Header(MessagePart? payload, string name) =>
        payload?.Headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTimeOffset? ToDate(long? internalDateMs) =>
        internalDateMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    private static IReadOnlyList<string> SplitAddresses(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ExtractBody(MessagePart? payload)
    {
        var plain = FindPart(payload, "text/plain");
        if (plain is not null) return MailText.Normalize(MailText.DecodeBase64Url(plain.Body?.Data));

        var html = FindPart(payload, "text/html");
        return html is not null ? MailText.HtmlToText(MailText.DecodeBase64Url(html.Body?.Data)) : string.Empty;
    }

    private static MessagePart? FindPart(MessagePart? part, string mimeType)
    {
        if (part is null) return null;
        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(part.Filename)
            && !string.IsNullOrEmpty(part.Body?.Data))
            return part;

        foreach (var child in part.Parts ?? Array.Empty<MessagePart>())
        {
            var found = FindPart(child, mimeType);
            if (found is not null) return found;
        }
        return null;
    }

    private static void CollectAttachments(MessagePart? part, List<string> names)
    {
        if (part is null) return;
        if (!string.IsNullOrEmpty(part.Filename)) names.Add(part.Filename);
        foreach (var child in part.Parts ?? Array.Empty<MessagePart>()) CollectAttachments(child, names);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _gate.Dispose();
    }
}
