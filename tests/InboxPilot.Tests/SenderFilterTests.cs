using InboxPilot.Mcp.Mail;

namespace InboxPilot.Tests;

public class SenderFilterTests
{
    [Theory]
    [InlineData("The Housing Experts srl - Syndic <syndic@thehousing.expert>", "syndic@thehousing.expert", true)]
    [InlineData("syndic@thehousing.expert", "syndic@thehousing.expert", true)]
    [InlineData("Other <other@thehousing.expert>", "syndic@thehousing.expert", false)]
    [InlineData("Other <other@thehousing.expert>", "thehousing.expert", true)]
    [InlineData("Other <other@thehousing.expert>", "@thehousing.expert", true)]
    [InlineData("x <a@nothousing.expert>", "thehousing.expert", false)]
    [InlineData("x <syndic@thehousing.expert.evil.com>", "syndic@thehousing.expert", false)]
    public void Matches_exact_address_or_domain(string from, string sender, bool expected) =>
        Assert.Equal(expected, SenderFilter.Matches(from, SenderFilter.Normalize(sender)));

    [Theory]
    [InlineData("a@b.c OR in:anywhere")]
    [InlineData("a@b.c\" -label:x")]
    [InlineData("")]
    public void Normalize_rejects_unsafe_input(string value) =>
        Assert.Throws<ArgumentException>(() => SenderFilter.Normalize(value));
}
