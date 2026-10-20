using InboxPilot.Mcp;
using InboxPilot.Mcp.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Content root = the exe folder, so appsettings load no matter which directory Claude Code starts us from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(); // re-added so env vars override the local file

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
