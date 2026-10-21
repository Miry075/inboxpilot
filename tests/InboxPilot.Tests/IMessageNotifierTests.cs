using InboxPilot.Mcp.Notifications;

namespace InboxPilot.Tests;

public class IMessageNotifierTests
{
    [Fact]
    public void Recipient_and_text_are_trailing_arguments_not_part_of_the_script()
    {
        var args = IMessageNotifier.BuildArguments("+32470000000", "hi \" & quit");

        Assert.Equal("+32470000000", args[^2]);
        Assert.Equal("hi \" & quit", args[^1]);
        Assert.DoesNotContain(args.Take(args.Count - 2), a => a.Contains("+32470000000") || a.Contains("quit"));
    }

    [Fact]
    public void Long_text_is_truncated()
    {
        var args = IMessageNotifier.BuildArguments("a@b.c", new string('x', 900));
        Assert.Equal(500, args[^1].Length);
    }
}
