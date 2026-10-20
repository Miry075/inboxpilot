# One triage run: Claude Code (your subscription) + the InboxPilot MCP server.
# Usage: .\scripts\run-triage.ps1 [-Model sonnet] [-MaxTurns 80] [-SinceHours 26]
param(
    [string]$Model = "sonnet",
    [int]$MaxTurns = 80,
    [int]$SinceHours = 26
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false   # no BOM: Claude Code parses these files as JSON/text

# Locate the published server
$exe = Join-Path $root "publish\InboxPilot.Mcp.exe"
$dll = Join-Path $root "publish\InboxPilot.Mcp.dll"
if (Test-Path $exe)     { $server = @{ command = $exe; args = @() } }
elseif (Test-Path $dll) { $server = @{ command = "dotnet"; args = @($dll) } }
else { throw "Server not published. Run .\scripts\publish.ps1 first." }

# Runtime folder (outside the repo, so no project CLAUDE.md or settings leak into the run)
$dataDir = Join-Path $env:LOCALAPPDATA "InboxPilot"
$runDir  = Join-Path $dataDir "run"
$logDir  = Join-Path $dataDir "logs"
New-Item -ItemType Directory -Force $runDir, $logDir | Out-Null

$mcpFile = Join-Path $runDir "mcp.json"
$json = @{ mcpServers = @{ inboxpilot = $server } } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($mcpFile, $json, $utf8)

$promptFile = Join-Path $runDir "system-prompt.md"
$prompt = (Get-Content (Join-Path $root "agent\triage-instructions.md") -Raw -Encoding UTF8) + "`n`n" +
          (Get-Content (Join-Path $root "agent\priorities.md") -Raw -Encoding UTF8) + "`n`n" +
          "Current local time: $(Get-Date -Format 'yyyy-MM-dd HH:mm') (Europe/Brussels)."
[IO.File]::WriteAllText($promptFile, $prompt, $utf8)

$settingsFile = Join-Path $root "agent\settings.json"
$log = Join-Path $logDir ("triage-{0:yyyyMMdd-HHmm}.log" -f (Get-Date))

Push-Location $runDir
# Windows PowerShell 5.1 turns native stderr lines into errors; "Stop" would abort the run on the first log line.
$ErrorActionPreference = "Continue"
try {
    & claude -p "Run the inbox triage now, following your instructions. Use sinceHours=$SinceHours." `
        --model $Model `
        --max-turns $MaxTurns `
        --permission-mode dontAsk `
        --settings $settingsFile `
        --append-system-prompt-file $promptFile `
        --no-session-persistence `
        --output-format text `
        --strict-mcp-config `
        --mcp-config $mcpFile 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

# Keep 30 days of logs
Get-ChildItem $logDir -Filter "triage-*.log" | Where-Object LastWriteTime -lt (Get-Date).AddDays(-30) | Remove-Item -ErrorAction SilentlyContinue
exit $code
