#!/usr/bin/env bash
# macOS scheduler: installs a LaunchAgent that runs triage at fixed times every day.
# Runs only while you are logged in (needs your Claude Code login in the Keychain).
# If the Mac is asleep at a scheduled time, launchd runs the job once when it wakes.
#
# Usage:
#   ./scripts/register-launchd.sh                          # 08:05 to 20:05 every 2 hours
#   ./scripts/register-launchd.sh 08:05 13:05 18:05        # custom times
#   ./scripts/register-launchd.sh --remove
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/_common.sh"

[[ "$(uname -s)" == "Darwin" ]] || die "macOS only. On Linux use cron: see README."

LABEL="com.inboxpilot.triage"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOMAIN="gui/$(id -u)"

launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true

if [[ "${1:-}" == "--remove" ]]; then
  rm -f "$PLIST"
  echo "Removed $LABEL."
  exit 0
fi

command -v claude >/dev/null || die "'claude' not found. Install Claude Code and run 'claude' once to sign in."
[[ -f "$ROOT/publish/InboxPilot.Mcp.dll" ]] || die "Server not published. Run ./scripts/publish.sh first."
chmod +x "$ROOT/scripts/run-triage.sh"

TIMES=("$@")
[[ ${#TIMES[@]} -gt 0 ]] || TIMES=(08:05 10:05 12:05 14:05 16:05 18:05 20:05)

INTERVALS=""
for t in "${TIMES[@]}"; do
  [[ "$t" =~ ^([0-9]{1,2}):([0-9]{2})$ ]] || die "Bad time '$t'. Use HH:MM."
  INTERVALS+="    <dict><key>Hour</key><integer>$((10#${BASH_REMATCH[1]}))</integer><key>Minute</key><integer>$((10#${BASH_REMATCH[2]}))</integer></dict>
"
done

DATA_DIR="$(inboxpilot_data_dir)"
mkdir -p "$DATA_DIR/logs" "$HOME/Library/LaunchAgents"

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>$ROOT/scripts/run-triage.sh</string>
  </array>
  <key>WorkingDirectory</key><string>$ROOT</string>
  <key>StartCalendarInterval</key>
  <array>
$INTERVALS  </array>
  <key>StandardOutPath</key><string>$DATA_DIR/logs/launchd.out.log</string>
  <key>StandardErrorPath</key><string>$DATA_DIR/logs/launchd.err.log</string>
  <key>ProcessType</key><string>Background</string>
</dict>
</plist>
EOF

plutil -lint "$PLIST" >/dev/null
launchctl bootstrap "$DOMAIN" "$PLIST"

echo "Registered $LABEL at: ${TIMES[*]}"
echo "Run now:  launchctl kickstart $DOMAIN/$LABEL"
echo "Remove:   ./scripts/register-launchd.sh --remove"
