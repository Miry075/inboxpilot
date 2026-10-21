# Shared helpers for the macOS/Linux scripts. Sourced, not executed.

die() { echo "Error: $*" >&2; exit 1; }

# launchd and cron start with a minimal PATH: add the usual install locations
# of dotnet (official pkg, Homebrew, dotnet-install script) and claude (native installer, npm).
for dir in \
  "/usr/local/share/dotnet" "$HOME/.dotnet" \
  "/opt/homebrew/bin" "/usr/local/bin" \
  "$HOME/.local/bin" "$HOME/.claude/local" "$HOME/.npm-global/bin"; do
  # if/fi rather than && so 'set -e' never trips on a missing folder (macOS ships bash 3.2)
  if [[ -d "$dir" && ":$PATH:" != *":$dir:"* ]]; then PATH="$dir:$PATH"; fi
done
export PATH

# Must match .NET's Environment.SpecialFolder.LocalApplicationData + "/InboxPilot",
# where the server stores tokens and reads inboxpilot.env.
inboxpilot_data_dir() {
  if [[ -n "${INBOXPILOT_DATA_DIR:-}" ]]; then
    printf '%s' "$INBOXPILOT_DATA_DIR"
  elif [[ "$(uname -s)" == "Darwin" ]]; then
    printf '%s' "$HOME/Library/Application Support/InboxPilot"   # .NET 8+ on macOS
  else
    printf '%s' "${XDG_DATA_HOME:-$HOME/.local/share}/InboxPilot"
  fi
}
