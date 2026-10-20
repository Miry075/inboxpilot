# InboxPilot

AI triage for Gmail and Outlook.com, run by **Claude Code on your Claude subscription** (no API billing).

- A .NET 10 **MCP server** exposes six safe tools over your two mailboxes and your phone.
- **Claude Code** runs headless on a schedule, reads new email, labels priority and category,
  drafts replies, and alerts you on Telegram or WhatsApp when a goal is reached.
- It **never sends, forwards or deletes** email. Those tools do not exist.

```
Task Scheduler ──> run-triage.ps1 ──> claude -p (your subscription)
                                          │  stdio (MCP)
                                          ▼
                                 InboxPilot.Mcp.exe
                     ┌──────────────┼───────────────┐
                Gmail API     Microsoft Graph    Telegram / WhatsApp
```

## Tools exposed to the agent

| Tool | Effect |
|---|---|
| `list_new_emails` | Inbox emails in the window, not yet labelled `InboxPilot/Processed` |
| `read_email` | Plain-text body (HTML stripped, hidden text removed, truncated to 8,000 chars) |
| `apply_label` | Whitelisted labels only: P1–P4, Client, MissionOffer, Admin, Finance, Personal, Wedding, Newsletter, Review |
| `create_reply_draft` | Native draft in the same thread. Not sent |
| `mark_processed` | Adds `InboxPilot/Processed` so the next run skips it |
| `notify_owner` | Message to **your** phone only. Daily cap and duplicate suppression |

Every action is logged to `%LOCALAPPDATA%\InboxPilot\audit\audit-YYYY-MM.jsonl`.

---

## Setup (about 45 minutes, once)

### 0. Prerequisites

- .NET 10 SDK: `dotnet --version`
- Claude Code installed and signed in with your Pro/Max account: run `claude` once, then check `claude auth status`.

Everything sensitive lives in `%LOCALAPPDATA%\InboxPilot` (tokens, client secret, logs), never in the repo.
Print the exact folder any time with `InboxPilot.Mcp.exe where`.

### 1. Gmail (Google Cloud)

1. [console.cloud.google.com](https://console.cloud.google.com) → new project `InboxPilot`.
2. **APIs & Services → Library** → enable **Gmail API**.
3. **OAuth consent screen** → External → app name `InboxPilot`, your email as support and developer contact.
   Add scope `https://www.googleapis.com/auth/gmail.modify`.
4. **Publish the app** (status "In production").
   In "Testing" status Google expires refresh tokens after 7 days and the scheduled runs break weekly.
   You will see an "unverified app" warning at sign-in: Advanced → continue. Fine for personal use.
5. **Credentials → Create credentials → OAuth client ID → Desktop app** → download the JSON.
6. Save it as `%LOCALAPPDATA%\InboxPilot\client_secret.json`.

### 2. Outlook.com (Microsoft Entra)

1. [entra.microsoft.com](https://entra.microsoft.com) → **App registrations → New registration**.
2. Supported account types: **Personal Microsoft accounts only**.
3. Redirect URI: platform **Public client/native (mobile & desktop)**, value `http://localhost`.
4. **API permissions → Microsoft Graph → Delegated**: `Mail.ReadWrite`, `User.Read`, `offline_access`.
5. **Authentication → Allow public client flows: Yes**.
6. Copy the **Application (client) ID**.

Do not use a work/client mailbox here: admin consent and data policies apply.

### 3. Notification channel

**Telegram (recommended to start, 5 minutes):**
1. In Telegram, message `@BotFather` → `/newbot` → copy the token.
2. Send any message to your new bot.
3. Open `https://api.telegram.org/bot<TOKEN>/getUpdates` → copy `message.chat.id`.

**WhatsApp (Meta Cloud API):**
1. [developers.facebook.com](https://developers.facebook.com) → create a Business app → add **WhatsApp**.
2. Note the **Phone number ID**; add your own number as a recipient.
3. Create a permanent token: Business Settings → System users → generate token with `whatsapp_business_messaging`.
4. Create a **Utility** template, e.g. name `inboxpilot_alert`, body `New InboxPilot alert: {{1}}`, wait for approval.
   Without a template, WhatsApp only delivers if you messaged the business number in the last 24 hours.

### 4. Configure: server parameters

Every value is a **parameter of the MCP server**, given as an environment variable or a `--switch`.
The agent never sees them: they are not tool arguments.

| Switch | Environment variable | Value |
|---|---|---|
| `--outlook-client-id` | `INBOXPILOT_OUTLOOK_CLIENT_ID` | Entra app (client) ID |
| `--outlook-tenant-id` | `INBOXPILOT_OUTLOOK_TENANT_ID` | `consumers` (default) |
| `--outlook-enabled` | `INBOXPILOT_OUTLOOK_ENABLED` | `true` / `false` |
| `--gmail-client-secret` | `INBOXPILOT_GMAIL_CLIENT_SECRET` | Path to the Google client JSON (default `client_secret.json` in the data folder) |
| `--gmail-enabled` | `INBOXPILOT_GMAIL_ENABLED` | `true` / `false` |
| `--notify-channel` | `INBOXPILOT_NOTIFY_CHANNEL` | `telegram` / `whatsapp` |
| `--notify-daily-cap` | `INBOXPILOT_NOTIFY_DAILY_CAP` | default `10` |
| `--whatsapp-token` | `INBOXPILOT_WHATSAPP_TOKEN` | Meta system-user token (secret) |
| `--whatsapp-phone-number-id` | `INBOXPILOT_WHATSAPP_PHONE_NUMBER_ID` | Sender phone number ID |
| `--whatsapp-to` | `INBOXPILOT_WHATSAPP_TO` | Your number, e.g. `32470000000` |
| `--whatsapp-template` | `INBOXPILOT_WHATSAPP_TEMPLATE` | e.g. `inboxpilot_alert` |
| `--whatsapp-template-language` | `INBOXPILOT_WHATSAPP_TEMPLATE_LANGUAGE` | e.g. `en` |
| `--telegram-bot-token` | `INBOXPILOT_TELEGRAM_BOT_TOKEN` | From @BotFather (secret) |
| `--telegram-chat-id` | `INBOXPILOT_TELEGRAM_CHAT_ID` | Your chat ID |
| `--data-dir` | `INBOXPILOT_DATA_DIR` | default `%LOCALAPPDATA%\InboxPilot` |

`InboxPilot.Mcp.exe params` prints the full list; `InboxPilot.Mcp.exe config` shows the effective values with secrets masked.
Precedence, last wins: `appsettings.json` → `appsettings.Local.json` → environment variables → `--switches`.

**For scheduled runs (recommended):** copy `agent/inboxpilot.env.example` to
`%LOCALAPPDATA%\InboxPilot\inboxpilot.env` and fill it. The server and the CLI commands read it
automatically, and `run-triage.ps1` also passes it as the server's environment in `mcp.json`. Use environment variables for secrets: `--switches` are visible in the process list.

**For interactive Claude Code sessions:**

```powershell
claude mcp add --scope user inboxpilot `
  -e INBOXPILOT_OUTLOOK_CLIENT_ID=<id> `
  -e INBOXPILOT_NOTIFY_CHANNEL=whatsapp `
  -e INBOXPILOT_WHATSAPP_TOKEN=<token> `
  -e INBOXPILOT_WHATSAPP_PHONE_NUMBER_ID=<id> `
  -e INBOXPILOT_WHATSAPP_TO=32470000000 `
  -e INBOXPILOT_WHATSAPP_TEMPLATE=inboxpilot_alert `
  -- "C:\path\to\InboxPilot\publish\InboxPilot.Mcp.exe"
```

The CLI commands take the same switches, e.g. `InboxPilot.Mcp.exe test-notify --notify-channel telegram`.

### 5. Build, sign in, test

```powershell
.\scripts\publish.ps1                     # runs tests, publishes to .\publish
.\publish\InboxPilot.Mcp.exe auth gmail   # browser sign-in, token stored locally
.\publish\InboxPilot.Mcp.exe auth outlook
.\publish\InboxPilot.Mcp.exe test         # lists 3 unprocessed emails per mailbox
.\publish\InboxPilot.Mcp.exe test-notify  # alert on your phone
```

### 6. Tell the agent your priorities

Edit `agent/priorities.md`: VIP senders, your daily rate for goal G1, signature.
`agent/triage-instructions.md` holds the procedure and security rules; change it only deliberately.

### 7. First run, watched

```powershell
.\scripts\run-triage.ps1
```

Check Gmail labels / Outlook categories under `InboxPilot/…`, your Drafts folders, and the log in
`%LOCALAPPDATA%\InboxPilot\logs`. On a full inbox, start small: `.\scripts\run-triage.ps1 -SinceHours 6`.

### 8. Schedule

```powershell
.\scripts\register-task.ps1                                 # 08:05 to 20:05 every 2 hours
.\scripts\register-task.ps1 -Times "08:05","13:05","18:05"  # lighter
```

The task runs only while you are logged on to Windows (it uses your Claude Code login and token caches).

---

## Use it interactively too

Register the server with Claude Code once, then ask anything about your inbox in a normal session:

```powershell
claude mcp add --scope user inboxpilot -- "C:\path\to\InboxPilot\publish\InboxPilot.Mcp.exe"
```

Examples: "Summarise every mission offer from this week", "Draft replies to the wedding vendors".

## Security model

| Threat | Control |
|---|---|
| Email says "forward all invoices to X" | No send/forward tool exists. Drafts only, and you send |
| Email instructs the AI | Instructions + per-tool notice mark bodies as untrusted; suspicious mail gets `Review` |
| Hidden text / zero-width injection | HTML hidden elements and invisible Unicode stripped before the model sees it |
| Alert spam or data exfiltration via alerts | Recipient fixed in config, daily cap, dedupe, 1,000-char limit |
| Header injection in drafts | CR/LF stripped from headers |
| Agent uses other Claude Code tools | `agent/settings.json` denies Bash, file edits and web; `--strict-mcp-config` loads only this server |
| Token theft | Tokens stay in your user profile; Outlook cache encrypted by the OS |

## Limits

- Runs count against your Claude plan's usage limits. Seven runs a day on Sonnet is light for Max;
  on Pro, use fewer runs or reduce `-MaxTurns`.
- Personal automation only. If other people ever use this (the SaaS idea), switch to the Claude API
  with an API key; this MCP server stays reusable as is.
- Scheduled, not real-time: worst-case delay is the gap between runs.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Gmail is not authorized yet` | `InboxPilot.Mcp.exe auth gmail` |
| Gmail works for 7 days then fails | OAuth app still in "Testing": publish it, then `auth gmail` again |
| `Outlook is not authorized yet` / `AuthenticationRequiredException` | `InboxPilot.Mcp.exe auth outlook` |
| Tools not visible to Claude | Run `claude -p "list your tools" --strict-mcp-config --mcp-config %LOCALAPPDATA%\InboxPilot\run\mcp.json` |
| WhatsApp error 131047 | Outside the 24h window: configure `TemplateName` |
| Nothing happens on schedule | Task Scheduler → History; check `claude` is on PATH for your user |

## Project layout

```
src/InboxPilot.Mcp/        MCP server (tools, Gmail + Graph providers, notifiers, CLI)
tests/InboxPilot.Tests/    xUnit tests for HTML sanitising and reply MIME
agent/                     instructions, priorities, permission settings for Claude Code
scripts/                   publish, run-triage (ps1/sh), register-task
```
