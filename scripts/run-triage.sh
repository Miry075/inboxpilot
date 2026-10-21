#!/usr/bin/env bash
# One triage run on macOS/Linux: Claude Code (your subscription) + the InboxPilot MCP server.
# Usage: ./scripts/run-triage.sh [model] [max_turns] [since_hours]
set -euo pipefail

MODEL="${1:-sonnet}"
MAX_TURNS="${2:-80}"
SINCE_HOURS="${3:-26}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/_common.sh"

DLL="$ROOT/publish/InboxPilot.Mcp.dll"
[[ -f "$DLL" ]] || die "Server not published. Run ./scripts/publish.sh first."
DOTNET="$(command -v dotnet)" || die "'dotnet' not found. Install the .NET 10 SDK."
CLAUDE="$(command -v claude)" || die "'claude' not found. Install Claude Code and sign in once."

DATA_DIR="$(inboxpilot_data_dir)"
RUN_DIR="$DATA_DIR/run"
LOG_DIR="$DATA_DIR/logs"
mkdir -p "$RUN_DIR" "$LOG_DIR"

# Server parameters: KEY=VALUE lines from inboxpilot.env become the server's environment.
ENV_FILE="${INBOXPILOT_ENV_FILE:-$DATA_DIR/inboxpilot.env}"
json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
trim() { printf '%s' "$1" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'; }
ENV_JSON=""
if [[ -f "$ENV_FILE" ]]; then
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="$(trim "$line")"
    [[ -z "$line" || "$line" == \#* || "$line" != *=* ]] && continue
    key="$(trim "${line%%=*}")"; value="$(trim "${line#*=}")"
    ENV_JSON+="${ENV_JSON:+, }\"$(json_escape "$key")\": \"$(json_escape "$value")\""
  done < "$ENV_FILE"
fi

umask 077  # mcp.json holds secrets
# Absolute dotnet path: launchd/cron runs with a minimal PATH.
cat > "$RUN_DIR/mcp.json" <<EOF
{ "mcpServers": { "inboxpilot": { "command": "$(json_escape "$DOTNET")", "args": ["$(json_escape "$DLL")"], "env": { $ENV_JSON } } } }
EOF

{
  cat "$ROOT/agent/triage-instructions.md"
  printf '\n\n'
  cat "$ROOT/agent/priorities.md"
  printf '\n\nCurrent local time: %s (Europe/Brussels).\n' "$(TZ=Europe/Brussels date '+%Y-%m-%d %H:%M')"
} > "$RUN_DIR/system-prompt.md"

LOG="$LOG_DIR/triage-$(date +%Y%m%d-%H%M).log"
cd "$RUN_DIR"   # outside the repo: no project CLAUDE.md or settings leak into the run
"$CLAUDE" -p "Run the inbox triage now, following your instructions. Use sinceHours=$SINCE_HOURS." \
  --model "$MODEL" \
  --max-turns "$MAX_TURNS" \
  --permission-mode dontAsk \
  --settings "$ROOT/agent/settings.json" \
  --append-system-prompt-file "$RUN_DIR/system-prompt.md" \
  --no-session-persistence \
  --output-format text \
  --strict-mcp-config \
  --mcp-config "$RUN_DIR/mcp.json" 2>&1 | tee "$LOG"

find "$LOG_DIR" -name 'triage-*.log' -mtime +30 -delete
