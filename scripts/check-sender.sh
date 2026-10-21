#!/usr/bin/env bash
# Usage: ./scripts/check-sender.sh [sender] [hours] [extra InboxPilot.Mcp switches...]
#   sender  address or domain, default syndic@thehousing.expert
#   hours   look-back window, default 24
# Examples:
#   ./scripts/check-sender.sh
#   ./scripts/check-sender.sh syndic@thehousing.expert 72 --notify true
#   ./scripts/check-sender.sh thehousing.expert 48 --account outlook
# Exit code: 0 = found, 10 = none, other = error.
set -euo pipefail
cd "$(dirname "$0")/.."
# shellcheck source=_common.sh
source scripts/_common.sh

SENDER="${1:-syndic@thehousing.expert}"
HOURS="${2:-24}"
[[ $# -ge 2 ]] && shift 2 || shift $#

exe="./publish/InboxPilot.Mcp"
[[ -x "$exe" ]] || die "Run ./scripts/publish.sh first."
exec "$exe" check-sender "$SENDER" --since-hours "$HOURS" "$@"
