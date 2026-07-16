<#
.SYNOPSIS
    Operator cues for the interactive GitHub Copilot CLI capture beats.

.DESCRIPTION
    This script never launches Copilot or changes personal Copilot settings.
    It prints the isolated --plugin-dir launch command and pauses while the
    operator drives real permission, elicitation, notification, compaction,
    subagent, interrupt, and session-end behavior in another terminal.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ScratchDir
)

$ErrorActionPreference = "Stop"

function Pause-ForBeat {
    param([string]$Message)
    Write-Host ""
    Write-Host $Message -ForegroundColor Cyan
    Read-Host "Press Enter after completing this beat in the separate Copilot terminal" | Out-Null
}

$ScratchDir = (Resolve-Path -LiteralPath $ScratchDir).Path
$pluginDir = Join-Path $ScratchDir "copilot-hostrec-plugin"
$activatePath = Join-Path $ScratchDir "activate.ps1"
if (-not (Test-Path -LiteralPath $pluginDir) -or -not (Test-Path -LiteralPath $activatePath)) {
    throw "The sandbox is not staged. Run stage.ps1 -ScratchDir '$ScratchDir' first."
}

. $activatePath

Write-Host "=== GitHub Copilot CLI supervised host-event capture ===" -ForegroundColor Magenta
Write-Host "Capture id:   $($env:HOSTREC_SESSION)"
Write-Host "Capture file: $(Join-Path $env:HOSTREC_CAPTURE_DIR ('session-{0}.jsonl' -f $env:HOSTREC_SESSION))"
Write-Host ""
Write-Host "In a SEPARATE fresh PowerShell terminal, run:" -ForegroundColor Yellow
Write-Host "  . '$activatePath'"
Write-Host "  Set-Location '$ScratchDir'"
Write-Host "  copilot --plugin-dir '$pluginDir' --no-auto-update"
Read-Host "Press Enter after the Copilot session has started" | Out-Null

Pause-ForBeat "BEAT 1 -- first prompt: send a normal prompt and let the main agent finish its turn."
Pause-ForBeat "BEAT 2 -- successful tools: ask it to list files, read sample.txt, and create or edit a disposable file."
Pause-ForBeat "BEAT 3 -- real permission prompt: run /reset-allowed-tools, then request a PowerShell command you have not approved in this sandbox. Approve the genuine prompt."
Pause-ForBeat "BEAT 4 -- elicitation: explicitly ask Copilot to use ask_user to ask you one multiple-choice question, then answer it."
Pause-ForBeat "BEAT 5 -- parallel subagents: request two concurrent subagents using named agents such as explore and task. Avoid general-purpose for this beat because current docs say it emits no subagentStart/subagentStop hooks."
Pause-ForBeat "BEAT 6 -- background notification: ask it to run scripts\slow.ps1 as a background shell task, continue briefly, and wait for the completion notification."
Pause-ForBeat "BEAT 7 -- tool failure: ask it to run scripts\fail.ps1 once and leave the intentional failure in place."
Pause-ForBeat "BEAT 8 -- compaction: run /compact manually and allow it to finish."
Pause-ForBeat "BEAT 9 -- real interrupt: start a longer response or tool call and press Esc to interrupt it. Do not terminate the whole Copilot process."
Pause-ForBeat "BEAT 10 -- clean session end: use /exit (or Ctrl+C twice) so sessionEnd can fire normally."

Write-Host ""
Write-Host "Capture complete. Leave the JSONL file uncommitted; only distilled findings belong in docs/host-events/copilot-cli.md." -ForegroundColor Green
