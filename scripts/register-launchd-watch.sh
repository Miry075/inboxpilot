#!/usr/bin/env bash
# macOS: polls for new emails from given senders and alerts you (no Claude, no tokens used).
# Runs only while you are logged in. First run only records existing emails; later runs alert on new ones.
#
# Usage:
#   ./scripts/register-launchd-watch.sh syndic@thehousing.expert            # every 5 minutes
#   ./scripts/register-launchd-watch.sh "a@x.com,thehousing.expert" 120     # several senders, every 120 s
#   ./scripts/register-launchd-watch.sh --remove
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/_common.sh"

[[ "$(uname -s)" == "Darwin" ]] || die "macOS only."

LABEL="com.inboxpilot.watch"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOMAIN="gui/$(id -u)"
launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true

if [[ "${1:-}" == "--remove" ]]; then
  rm -f "$PLIST"; echo "Removed $LABEL."; exit 0
fi

SENDERS="${1:-}"; SECONDS_EVERY="${2:-300}"
[[ -n "$SENDERS" ]] || die "Usage: $0 <sender[,sender...]> [interval-seconds]"
[[ "$SENDERS" =~ ^[A-Za-z0-9@._+,-]+$ ]] || die "Sender list may only contain letters, digits and @ . _ + - ,"
[[ "$SECONDS_EVERY" =~ ^[0-9]+$ && "$SECONDS_EVERY" -ge 60 ]] || die "Interval must be a number of seconds >= 60."
[[ -x "$ROOT/publish/InboxPilot.Mcp" ]] || die "Run ./scripts/publish.sh first."

DATA_DIR="$(inboxpilot_data_dir)"
mkdir -p "$DATA_DIR/logs" "$HOME/Library/LaunchAgents"

cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$ROOT/publish/InboxPilot.Mcp</string>
    <string>check-sender</string>
    <string>$SENDERS</string>
    <string>--since-hours</string><string>48</string>
    <string>--only-new</string><string>true</string>
    <string>--notify</string><string>true</string>
  </array>
  <key>WorkingDirectory</key><string>$ROOT</string>
  <key>StartInterval</key><integer>$SECONDS_EVERY</integer>
  <key>RunAtLoad</key><true/>
  <key>StandardOutPath</key><string>$DATA_DIR/logs/watch.out.log</string>
  <key>StandardErrorPath</key><string>$DATA_DIR/logs/watch.err.log</string>
  <key>ProcessType</key><string>Background</string>
</dict>
</plist>
PLISTEOF

plutil -lint "$PLIST" >/dev/null
launchctl bootstrap "$DOMAIN" "$PLIST"
echo "Watching '$SENDERS' every ${SECONDS_EVERY}s (log: $DATA_DIR/logs/watch.out.log)"
echo "Remove: ./scripts/register-launchd-watch.sh --remove"
