#!/usr/bin/env bash
# Builds, tests and publishes the MCP server to ./publish (macOS/Linux).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/_common.sh"
command -v dotnet >/dev/null || die "'dotnet' not found. Install the .NET 10 SDK."

dotnet test "$ROOT/InboxPilot.slnx" -c Release
dotnet publish "$ROOT/src/InboxPilot.Mcp/InboxPilot.Mcp.csproj" -c Release -o "$ROOT/publish"

echo "Published to $ROOT/publish"
echo "Run the CLI with: $ROOT/publish/InboxPilot.Mcp <command>   (or: dotnet $ROOT/publish/InboxPilot.Mcp.dll <command>)"
