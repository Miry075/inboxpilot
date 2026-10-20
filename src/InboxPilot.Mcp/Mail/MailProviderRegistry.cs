namespace InboxPilot.Mcp.Mail;

public sealed class MailProviderRegistry(IEnumerable<IMailProvider> providers)
{
    private readonly IReadOnlyList<IMailProvider> _all = providers.ToList();

    public IReadOnlyList<IMailProvider> Enabled => _all.Where(p => p.IsEnabled).ToList();

    public IMailProvider Get(string account)
    {
        var provider = Enabled.FirstOrDefault(p => p.Account.Equals(account?.Trim(), StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new ArgumentException(
            $"Unknown or disabled account '{account}'. Enabled: {string.Join(", ", Enabled.Select(p => p.Account))}.");
    }

    public IReadOnlyList<IMailProvider> Resolve(string? account) =>
        string.IsNullOrWhiteSpace(account) || account.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Enabled
            : [Get(account)];
}
