using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Configuration;

namespace InboxPilot.Tests;

public class InboxPilotParametersTests
{
    [Fact]
    public void Switches_map_to_configuration_keys()
    {
        var config = new ConfigurationBuilder()
            .AddInboxPilotCommandLineParameters(
                ["auth", "gmail", "--whatsapp-token", "EAAGsecret", "--notify-channel=whatsapp", "--outlook-client-id", "abc"])
            .Build();

        Assert.Equal("EAAGsecret", config["InboxPilot:Notify:WhatsApp:AccessToken"]);
        Assert.Equal("whatsapp", config["InboxPilot:Notify:Channel"]);
        Assert.Equal("abc", config["InboxPilot:Outlook:ClientId"]);
    }

    [Fact]
    public void Positional_verbs_are_not_treated_as_switches()
    {
        Assert.Equal(new[] { "--data-dir", "C:\\x" }, InboxPilotParameters.SwitchArguments(["test", "--data-dir", "C:\\x", "outlook"]));
    }

    [Fact]
    public void Environment_parameters_are_read()
    {
        Environment.SetEnvironmentVariable("INBOXPILOT_TELEGRAM_CHAT_ID", "4242");
        try
        {
            var config = new ConfigurationBuilder().AddInboxPilotEnvironmentParameters().Build();
            Assert.Equal("4242", config["InboxPilot:Notify:Telegram:ChatId"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INBOXPILOT_TELEGRAM_CHAT_ID", null);
        }
    }

    [Fact]
    public void Env_file_is_parsed_and_unknown_keys_ignored()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "# comment",
                "INBOXPILOT_WHATSAPP_TO = 32470000000",
                "UNRELATED=1",
                "INBOXPILOT_WHATSAPP_TOKEN=abc=def"
            ]);
            var config = new ConfigurationBuilder().AddInboxPilotEnvFile(path).Build();

            Assert.Equal("32470000000", config["InboxPilot:Notify:WhatsApp:To"]);
            Assert.Equal("abc=def", config["InboxPilot:Notify:WhatsApp:AccessToken"]);
            Assert.Null(config["UNRELATED"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Every_parameter_has_unique_switch_and_env_name()
    {
        Assert.Equal(InboxPilotParameters.All.Count, InboxPilotParameters.All.Select(p => p.Switch).Distinct().Count());
        Assert.Equal(InboxPilotParameters.All.Count, InboxPilotParameters.All.Select(p => p.EnvironmentVariable).Distinct().Count());
    }

    [Fact]
    public void Mask_hides_secrets()
    {
        Assert.Equal("EAAG******", InboxPilotParameters.Mask("EAAGverysecret"));
        Assert.Equal("(empty)", InboxPilotParameters.Mask(""));
    }
}
