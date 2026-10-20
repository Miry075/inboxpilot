#!/usr/bin/env bash
# One triage run on macOS/Linux. Usage: ./scripts/run-triage.sh [model] [max_turns] [since_hours]
set -euo pipefail

MODEL="${1:-sonnet}"
MAX_TURNS="${2:-80}"
SINCE_HOURS="${3:-26}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DLL="$ROOT/publish/InboxPilot.Mcp.dll"
[[ -f "$DLL" ]] || { echo "Server not published. Run: dotnet publish src/InboxPilot.Mcp -c Release -o publish" >&2; exit 1; }

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/InboxPilot"
RUN_DIR="$DATA_DIR/run"
LOG_DIR="$DATA_DIR/logs"
mkdir -p "$RUN_DIR" "$LOG_DIR"

cat > "$RUN_DIR/mcp.json" <<EOF
{ "mcpServers": { "inboxpilot": { "command": "dotnet", "args": ["$DLL"] } } }
EOF

{
  cat "$ROOT/agent/triage-instructions.md"
  printf '\n\n'
  cat "$ROOT/agent/priorities.md"
  printf '\n\nCurrent local time: %s (Europe/Brussels).\n' "$(TZ=Europe/Brussels date '+%Y-%m-%d %H:%M')"
} > "$RUN_DIR/system-prompt.md"

LOG="$LOG_DIR/triage-$(date +%Y%m%d-%H%M).log"
cd "$RUN_DIR"
claude -p "Run the inbox triage now, following your instructions. Use sinceHours=$SINCE_HOURS." \
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
