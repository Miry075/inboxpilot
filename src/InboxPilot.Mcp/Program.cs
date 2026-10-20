using InboxPilot.Mcp;
using InboxPilot.Mcp.Cli;
using InboxPilot.Mcp.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Content root = the exe folder, so appsettings load no matter which directory Claude Code starts us from.
// Args are not handed to the host: the parameter provider below adds them with short-switch mappings.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});

// Precedence, last wins:
// appsettings.json < appsettings.Local.json < inboxpilot.env < env vars < INBOXPILOT_* env vars < --switches
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddInboxPilotEnvFile()
    .AddEnvironmentVariables()
    .AddInboxPilotEnvironmentParameters()
    .AddInboxPilotCommandLineParameters(args);

// stdout belongs to the MCP protocol: every log line goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddInboxPilot(builder.Configuration);

if (CliCommands.IsCommand(args))
{
    using var cliHost = builder.Build();
    return await CliCommands.RunAsync(cliHost.Services, args);
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
