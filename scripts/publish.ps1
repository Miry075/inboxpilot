# Builds, tests and publishes the MCP server to .\publish
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet test (Join-Path $root "InboxPilot.slnx") -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

dotnet publish (Join-Path $root "src\InboxPilot.Mcp\InboxPilot.Mcp.csproj") -c Release -o (Join-Path $root "publish")
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host "Published to $(Join-Path $root 'publish')"
