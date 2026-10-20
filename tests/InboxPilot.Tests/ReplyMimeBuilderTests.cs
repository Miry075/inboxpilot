using System.Text;
using InboxPilot.Mcp.Mail;

namespace InboxPilot.Tests;

public class ReplyMimeBuilderTests
{
    private static string Decode(string raw) => MailText.DecodeBase64Url(raw);

    [Fact]
    public void Adds_threading_headers()
    {
        var mime = Decode(ReplyMimeBuilder.BuildRaw(
            "client@example.com", "Mission .NET", "<abc@mail>", "<root@mail>", "Merci"));

        Assert.Contains("To: client@example.com\r\n", mime);
        Assert.Contains("Subject: Re: Mission .NET\r\n", mime);
        Assert.Contains("In-Reply-To: <abc@mail>\r\n", mime);
        Assert.Contains("References: <root@mail> <abc@mail>\r\n", mime);
    }

    [Fact]
    public void Does_not_double_prefix_subject()
    {
        Assert.Equal("RE: Offer", ReplyMimeBuilder.ReplySubject("RE: Offer"));
    }

    [Fact]
    public void Blocks_header_injection()
    {
        var mime = Decode(ReplyMimeBuilder.BuildRaw(
            "victim@example.com\r\nBcc: attacker@evil.com", "Hi", null, null, "x"));

        Assert.DoesNotContain("\r\nBcc:", mime);
    }

    [Fact]
    public void Encodes_non_ascii_subject_and_body()
    {
        var mime = Decode(ReplyMimeBuilder.BuildRaw("a@b.c", "Réunion", null, null, "Très bien"));

        Assert.Contains("Subject: =?UTF-8?B?", mime);
        var body = mime[(mime.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..].Trim();
        Assert.Equal("Très bien", Encoding.UTF8.GetString(Convert.FromBase64String(body.Replace("\r\n", ""))));
    }
}
