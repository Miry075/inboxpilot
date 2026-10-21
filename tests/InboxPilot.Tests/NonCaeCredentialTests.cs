using Azure.Core;
using InboxPilot.Mcp.Mail;

namespace InboxPilot.Tests;

public class NonCaeCredentialTests
{
    private sealed class Recording : TokenCredential
    {
        public TokenRequestContext? Last;

        public override AccessToken GetToken(TokenRequestContext c, CancellationToken ct)
        {
            Last = c;
            return new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext c, CancellationToken ct) =>
            new(GetToken(c, ct));
    }

    [Fact]
    public async Task Forces_cae_off_and_keeps_scopes_and_claims()
    {
        var inner = new Recording();
        var credential = new OutlookProvider.NonCaeCredential(inner);

        await credential.GetTokenAsync(
            new TokenRequestContext(["https://graph.microsoft.com/Mail.ReadWrite"], claims: "c", isCaeEnabled: true),
            CancellationToken.None);

        Assert.False(inner.Last!.Value.IsCaeEnabled);
        Assert.Equal("https://graph.microsoft.com/Mail.ReadWrite", inner.Last.Value.Scopes[0]);
        Assert.Equal("c", inner.Last.Value.Claims);
    }
}
