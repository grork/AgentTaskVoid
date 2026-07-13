<#
.SYNOPSIS
    Scripted-beat driver for the Claude Code capture (phase-14 Part B,
    INFRA-28). Drives ONLY the beats Claude Code's `-p`/print scripting
    surface genuinely reaches: fresh start, first prompt, tool calls, and
    parallel subagent fan-out (>= 2 concurrent).

.DESCRIPTION
    `-p` is one-shot: the process exits after the response. There is no idle
    window (no `idle_prompt`), no interactive permission dialog (a tool the
    session isn't pre-authorized for would just stall/fail non-interactively,
    not raise a real `permission_prompt`), and "interrupt" degenerates to a
    process kill. Those three beats -- plus the added subagent-permission
    beat and clean exit / SessionEnd -- are NOT scripted here; they belong to
    the supervised cue-script session (cue-script.ps1).

    This driver only exercises events the safe/skip matrix
    (docs/host-events/claude-code.md) has cleared. It never drives
    `WorktreeCreate` (the one skip-classified event) and does not need to --
    nothing in the scripted prompt touches worktrees.

    Because the real permission-prompt beat is deliberately NOT part of this
    script, the driven session runs with `--permission-mode bypassPermissions`
    so the scripted tool-call / fan-out beats can't stall waiting for a
    decision this non-interactive run has no way to answer. That is a
    property of THIS scripted run only -- the supervised cue-script session
    runs with normal permissions specifically so a real permission prompt can
    fire.

.PARAMETER ScratchDir
    The scratch project directory produced by stage.ps1 (its
    .claude\settings.json is what wires the recorder in). Required.

.PARAMETER Prompt
    Overrides the default beat-corpus prompt. The default prompt is a
    starting point -- tune it on the day if Claude Code's actual behavior
    (e.g. how readily it reaches for the Task tool) needs a stronger nudge to
    genuinely reach 2+ concurrent subagents.

.EXAMPLE
    $staged = . .\stage.ps1
    .\driver-scripted.ps1 -ScratchDir $staged.ScratchDir
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ScratchDir,

    [string]$Prompt = @"
List the files in the current directory, then read the contents of one of
them. After that, use the Task tool to launch two subagents IN PARALLEL (a
single message with two Task tool calls, not sequential): have one subagent
reverse the string "alpha" and report the result, and the other reverse the
string "beta" and report the result. Wait for both subagents to finish, then
report both reversed strings in your final reply.
"@
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ScratchDir)) {
    throw "Scratch dir '$ScratchDir' does not exist -- run stage.ps1 first."
}
$settingsPath = Join-Path $ScratchDir ".claude\settings.json"
if (-not (Test-Path $settingsPath)) {
    throw "No staged conduit config at '$settingsPath' -- run stage.ps1 first."
}
if (-not $env:HOSTREC_SESSION -or -not $env:HOSTREC_CAPTURE_DIR) {
    $activate = Join-Path $ScratchDir "activate.ps1"
    if (Test-Path $activate) {
        Write-Host "HOSTREC_SESSION/HOSTREC_CAPTURE_DIR not set in this shell -- dot-sourcing $activate" -ForegroundColor Yellow
        . $activate
    } else {
        throw "HOSTREC_SESSION/HOSTREC_CAPTURE_DIR are not set and no activate.ps1 was found under '$ScratchDir'. Dot-source stage.ps1 first: . .\stage.ps1"
    }
}

Write-Host "==> Driving scripted beats (session '$($env:HOSTREC_SESSION)')..." -ForegroundColor Cyan
Write-Host "    cwd -> $ScratchDir (so the project-scoped .claude\settings.json above applies)"

Push-Location $ScratchDir
try {
    & claude -p $Prompt --permission-mode bypassPermissions | Out-Host
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    Write-Warning "claude -p exited with code $exitCode -- the session may still have produced useful captures (check the JSONL); this is not necessarily fatal to the capture run."
}

$captureFile = Join-Path $env:HOSTREC_CAPTURE_DIR ("session-{0}.jsonl" -f $env:HOSTREC_SESSION)
Write-Host ""
Write-Host "==> Scripted beats done. Expect events in: $captureFile" -ForegroundColor Cyan
Write-Host "    Remaining beats (real permission prompt, user interrupt, idle wait," -ForegroundColor Yellow
Write-Host "    clean exit / SessionEnd, subagent-permission) need the supervised" -ForegroundColor Yellow
Write-Host "    cue-script.ps1 session -- this driver does not attempt them." -ForegroundColor Yellow
