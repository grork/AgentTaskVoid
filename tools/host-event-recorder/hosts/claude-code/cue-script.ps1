<#
.SYNOPSIS
    Supervised-capture cue script for the Claude Code leg of the host-event
    recorder (phase-14 Part B, INFRA-28). Walks the operator through the
    interactive-only beats the scripted driver (driver-scripted.ps1) cannot
    reach: a REAL permission prompt, a user interrupt, an idle wait past the
    notification threshold, clean exit / SessionEnd, and the Claude-Code-added
    subagent-originated-permission-prompt beat.

.DESCRIPTION
    *** REQUIRES PowerShell 7+ (pwsh). *** This is a dev-box diagnostic
    script, not a shipped end-user artifact -- the PS-5.1-compatible-subset
    discipline that binds `integrations/claude-code/translate.ps1` does not
    apply here (INFRA-28: "driver tech is unconstrained -- dev-box
    diagnostics, not a shipped artifact").

    FIRST action: disables the operator's real, installed, user-wide atv
    hooks (~/.claude/settings.json's "hooks" key) so this capture observes a
    clean baseline uncontaminated by the shipped phase-13 atv integration.
    LAST action: restores them verbatim, byte-for-byte, from a backup taken
    before the disable -- never a remembered manual edit, so a forgotten
    "before" can't contaminate the baseline and a forgotten "after" can't
    leave the operator's real sessions cardless.

    This script never launches `claude` itself. Between cues it pauses
    (Read-Host) while YOU drive an actual interactive `claude` session in a
    SEPARATE terminal window, cd'd into the staged scratch project dir, so
    this console stays free for the checkpoint prompts. No PTY automation --
    a human types the interrupt / answers the permission dialog / waits out
    the idle clock for real (INFRA-28: "never fake the signal under test").

    Safe to re-run: Disable-RealAtvHooks refuses to clobber an existing
    backup (so a previous run's un-restored backup is never silently
    overwritten), and Restore-RealAtvHooks is a no-op if there is nothing to
    restore.

.PARAMETER ScratchDir
    The scratch project directory produced by stage.ps1. Required -- run
    stage.ps1 first (or pass -RunStage to have this script call it).

.PARAMETER RunStage
    If set, dot-sources stage.ps1 itself before beginning (equivalent to
    running `. .\stage.ps1` yourself first). Otherwise this script assumes
    HOSTREC_SESSION / HOSTREC_CAPTURE_DIR are already set (e.g. via
    stage.ps1's activate.ps1) and -ScratchDir already points at a staged
    project.

.PARAMETER RealSettingsPath
    The operator's real, installed, user-wide Claude Code settings file.
    Defaults to $env:USERPROFILE\.claude\settings.json (Windows). Change only
    if the real integration is installed project-scoped instead.

.NOTES
    This script is AUTHORED to be run BY THE OPERATOR. It is deliberately not
    invoked by the executor that built it (phase-14 Part B guardrail): running
    Disable-RealAtvHooks against a live settings.json mid-session could
    disrupt whichever Claude Code session is currently reading it.
#>
[CmdletBinding()]
param(
    [string]$ScratchDir,
    [switch]$RunStage,
    [string]$RealSettingsPath = (Join-Path $env:USERPROFILE ".claude\settings.json")
)

$ErrorActionPreference = "Stop"

function Pause-ForBeat {
    param([string]$Message)
    Write-Host ""
    Write-Host $Message -ForegroundColor Cyan
    Read-Host "Press Enter once you've done this (in the SEPARATE claude terminal)" | Out-Null
}

function Disable-RealAtvHooks {
    param([string]$SettingsPath)

    if (-not (Test-Path $SettingsPath)) {
        Write-Host "[disable] No file at '$SettingsPath' -- nothing installed, nothing to disable." -ForegroundColor Yellow
        return
    }

    $backupPath = "$SettingsPath.atv-disabled-backup"
    if (Test-Path $backupPath) {
        throw "[disable] Backup '$backupPath' already exists -- a previous capture run's re-enable step never completed. Resolve that manually (compare it against '$SettingsPath' and restore/delete as appropriate) before starting a new capture."
    }

    # Verbatim byte copy -- this file, and only this file, is restored from
    # this backup at the end. Never re-serialized.
    Copy-Item -Path $SettingsPath -Destination $backupPath

    $raw = Get-Content -Raw -Path $SettingsPath
    $obj = $raw | ConvertFrom-Json -AsHashtable
    if ($obj.ContainsKey("hooks")) {
        $obj.Remove("hooks") | Out-Null
        ($obj | ConvertTo-Json -Depth 30) | Set-Content -Path $SettingsPath -Encoding utf8 -NoNewline
        Write-Host "[disable] Removed 'hooks' from '$SettingsPath'. Backup: '$backupPath'." -ForegroundColor Green
    }
    else {
        Write-Host "[disable] '$SettingsPath' has no 'hooks' key right now -- nothing to strip. Backup kept at '$backupPath' anyway, for symmetry with the re-enable step." -ForegroundColor Yellow
    }
}

function Restore-RealAtvHooks {
    param([string]$SettingsPath)

    $backupPath = "$SettingsPath.atv-disabled-backup"
    if (-not (Test-Path $backupPath)) {
        Write-Host "[re-enable] No backup at '$backupPath' -- nothing to restore." -ForegroundColor Yellow
        return
    }

    # Restore the ORIGINAL bytes verbatim -- no JSON round-trip, so whatever
    # the file looked like before -disable- is exactly what's there after.
    Copy-Item -Path $backupPath -Destination $SettingsPath -Force
    Remove-Item -Path $backupPath -Force
    Write-Host "[re-enable] Restored '$SettingsPath' from backup and removed the backup sidecar." -ForegroundColor Green
}

# =============================================================================
Write-Host "=== Claude Code supervised host-event capture ===" -ForegroundColor Magenta
Write-Host "Real settings file: $RealSettingsPath"

if ($RunStage) {
    Write-Host "==> Staging (RunStage requested)..." -ForegroundColor Cyan
    $staged = . (Join-Path $PSScriptRoot "stage.ps1")
    $ScratchDir = $staged.ScratchDir
}
if (-not $ScratchDir) {
    throw "‑ScratchDir is required (or pass -RunStage). Run stage.ps1 first: `$staged = . .\stage.ps1` then pass -ScratchDir `$staged.ScratchDir."
}
if (-not $env:HOSTREC_SESSION -or -not $env:HOSTREC_CAPTURE_DIR) {
    $activate = Join-Path $ScratchDir "activate.ps1"
    if (Test-Path $activate) { . $activate }
    else { throw "HOSTREC_SESSION/HOSTREC_CAPTURE_DIR not set and no activate.ps1 under '$ScratchDir'. Run stage.ps1 first." }
}

Write-Host "Session id:  $($env:HOSTREC_SESSION)"
Write-Host "Capture dir: $($env:HOSTREC_CAPTURE_DIR)"
Write-Host "Scratch dir: $ScratchDir"
Write-Host ""
Write-Host "In a SEPARATE terminal, cd into the scratch dir now (so the staged" -ForegroundColor Yellow
Write-Host "project-scoped .claude\settings.json applies):" -ForegroundColor Yellow
Write-Host "  Set-Location '$ScratchDir'" -ForegroundColor Yellow
Read-Host "Press Enter once that terminal is ready (don't start claude yet)" | Out-Null

# --- FIRST: disable the operator's real hooks -------------------------------
Write-Host ""
Write-Host "=== STEP 1 (FIRST): disabling your real, installed atv hooks ===" -ForegroundColor Magenta
Disable-RealAtvHooks -SettingsPath $RealSettingsPath

try {
    # --- Beat corpus (LIFE-24) + the Claude-Code-added beat -----------------
    Pause-ForBeat "BEAT 1 -- fresh start: in the other terminal, start a fresh session: 'claude' (interactive, no --resume/--continue)."

    Pause-ForBeat "BEAT 2 -- first prompt: send any first prompt and let it respond."

    Pause-ForBeat "BEAT 3 -- tool calls: ask it to do a couple of real tool calls (e.g. 'list files here, then read one')."

    Pause-ForBeat "BEAT 4 -- parallel subagent fan-out (>= 2): ask explicitly for >= 2 CONCURRENT subagents, e.g. 'use the Task tool to launch two subagents in parallel, one to reverse the string alpha, one to reverse beta'. Confirm in the transcript they actually ran concurrently, not sequentially."

    Pause-ForBeat "BEAT 5 -- REAL permission prompt: ask for a tool call you have NOT pre-authorized in this session/profile (default permission mode, not bypassPermissions) so an actual permission dialog appears -- then approve it. Never fake this: it must be a genuine dialog, not a synthesized notification."

    Pause-ForBeat "BEAT 6 (ADDED beat, Claude-Code-specific) -- subagent-originated permission prompt: ask for a subagent task that itself needs a tool NOT pre-authorized, so the permission dialog is raised from INSIDE a subagent, not the main thread. This is the one that answers whether Notification: permission_prompt carries agent_id (LIFE-24 empirical item 2). Approve it when it appears."

    Pause-ForBeat "BEAT 7 -- user interrupt: mid-response (while it's actively working), press Escape (or Ctrl+C, whichever this Claude Code build uses) to interrupt it for real."

    Pause-ForBeat "BEAT 8 -- idle wait: after the interrupt (or after any turn completes), literally wait past the idle-notification threshold without sending anything. Watch for whether/when a notification fires (LIFE-24 empirical item 3: does idle_prompt fire after an interrupt, and on what timing/repetition)."

    Pause-ForBeat "BEAT 9 -- clean exit: end the session the normal way (/exit or closing the terminal via the standard path, NOT a hard kill) so SessionEnd has a chance to fire and prove the sync-at-teardown posture."

    Write-Host ""
    Write-Host "All beats cued. Capture file should be at:" -ForegroundColor Cyan
    Write-Host "  $(Join-Path $env:HOSTREC_CAPTURE_DIR ('session-{0}.jsonl' -f $env:HOSTREC_SESSION))"
    Write-Host "(remember: a fresh 'claude' invocation may mint its OWN session_id in the" -ForegroundColor Yellow
    Write-Host "payload that differs from HOSTREC_SESSION -- HOSTREC_SESSION only names the" -ForegroundColor Yellow
    Write-Host "capture FILE; the payload's own session_id field is a separate, host-side id" -ForegroundColor Yellow
    Write-Host "to record in the findings table alongside it.)" -ForegroundColor Yellow
    Read-Host "Press Enter when you're ready to restore your real hooks" | Out-Null
}
finally {
    # --- LAST: re-enable the operator's real hooks, always, even on error ---
    Write-Host ""
    Write-Host "=== STEP LAST: re-enabling your real atv hooks ===" -ForegroundColor Magenta
    Restore-RealAtvHooks -SettingsPath $RealSettingsPath
}

Write-Host ""
Write-Host "Done. Distill confirmed findings (including did-not-fire results) into" -ForegroundColor Green
Write-Host "docs/host-events/claude-code.md's Findings section, citing this session id" -ForegroundColor Green
Write-Host "and today's date + 'claude --version'." -ForegroundColor Green
