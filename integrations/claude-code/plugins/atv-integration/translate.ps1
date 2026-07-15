<#
.SYNOPSIS
    Translates one Claude Code hook event into an Agentaskvoid (atv) v2
    semantic verb call. Bundled + wired by the atv-integration Claude Code
    plugin (plugin.json / hooks/hooks.json) -- never invoked directly by a
    human, and never shipped inside the atv MSIX (LIFE-10's no-host-specifics
    invariant: atv itself knows nothing about Claude Code; all host-specific
    logic lives here).

.DESCRIPTION
    Windows PowerShell 5.1-compatible subset (DIST-4's paste-and-go posture --
    the only in-box JSON-capable runtime on any Windows machine; prefers
    nothing else, but also runs fine under pwsh 7 if present).

    The four LIFE-25 translator disciplines, each mapped to a real phase-13/14
    bug class:
      1. A real -File script (this file), never an embedded one-liner --
         hooks.json invokes it via the plain program+args exec form.
      2. Arbitrary text (goal/label/question/summary/detail) reaches atv via
         stdin ("--flag -"), never argv -- see Invoke-Atv below.
      3. Explicit UTF-8 at both ends: this script's OWN stdin (the Claude
         Code payload) is read via a UTF-8 StreamReader; $OutputEncoding is
         pinned before piping free text into atv's stdin.
      4. Payload fragments are never re-serialized -- Get-Prop hands back the
         already-JSON-decoded native string value straight from
         ConvertFrom-Json; nothing here re-encodes a subtree back to JSON
         text before handing it to atv.

    Exit-0 always (FAIL-1): every atv invocation is wrapped so a missing atv,
    a refused claim, or any other failure never surfaces to Claude Code as a
    nonzero hook exit. --strict is never passed.

    map.json (same directory) holds the event-independent DATA this script
    consults: tool name -> ERGO-31 kind, tool name -> the tool_input field
    that supplies the activity/question label, the StopFailure reason-token
    map, and the one tool name (Agent) whose Pre/PostToolUse events are
    suppressed (subagent spawn/retire is agent-started/agent-stopped, never
    an activity line -- docs/integration-api.md SS3). atv itself never reads
    map.json; it is a translator-only convention.

.PARAMETER Event
    The Claude Code hook_event_name this invocation is handling (SessionStart,
    UserPromptSubmit, PreToolUse, PostToolUse, PermissionRequest, Notification,
    Stop, StopFailure, SubagentStart, SubagentStop, SessionEnd).

.PARAMETER ProjectDir
    ERGO-30's --cwd anchor. hooks.json passes the literal string
    "${CLAUDE_PROJECT_DIR}", which Claude Code substitutes with the real
    project root BEFORE spawning this process (no JSON parse, no escaping
    trap) -- see docs/integration-api.md SS12. Forwarded on every upserting
    atv call; omitted entirely when absent (costs nothing).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Event,

    [Parameter(Mandatory = $false)]
    [string]$ProjectDir
)

$ErrorActionPreference = "Stop"
# Discipline 3: pin the encoding PowerShell uses when piping text into atv's
# stdin to UTF-8 WITHOUT a byte-order mark -- the plain [Text.Encoding]::UTF8
# singleton emits a leading BOM on every write, which would otherwise land as
# a stray U+FEFF character at the start of every goal/label/question/summary/
# detail atv receives.
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# ---- atv command resolution -------------------------------------------------
# Production: the bare alias "atv" resolved via PATH (the AppExecutionAlias
# every install -- dev-loop, test, or release -- registers). Offline
# translator tests point $env:ATV_TRANSLATOR_STUB_EXE at a compiled stub exe
# that records argv + stdin instead of the real atv.exe (a genuine separate
# process, exactly like the real atv.exe -- never an in-process "& script.ps1"
# invocation, which would route piped text through PowerShell's own pipeline
# objects instead of a real OS stdin handle). See
# tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs. This env
# var is a translator-test-only seam -- atv.exe itself has no knowledge of it.
$script:AtvStubExe = $env:ATV_TRANSLATOR_STUB_EXE
$script:AtvIsOverridden = -not [string]::IsNullOrEmpty($script:AtvStubExe)

function Get-Prop {
    param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $null }
    $prop = $Obj.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Get-CwdArgs {
    if ([string]::IsNullOrEmpty($ProjectDir)) { return @() }
    return @("--cwd", $ProjectDir)
}

function Get-TitleArgs {
    # ERGO-33's chain top: the host's own session name, forwarded as --title
    # ONLY when the user actually named this session -- absent/empty means
    # nothing rides argv at all, and the chain falls through to the repo/
    # folder built-in default (SemanticEngine.ApplyRepoDefaults). Same
    # present-only-when-non-empty convention as Get-CwdArgs above.
    param($SessionTitle)
    if ([string]::IsNullOrEmpty($SessionTitle)) { return @() }
    return @("--title", $SessionTitle)
}

function Invoke-Atv {
    # Discipline 2: $StdinText (when supplied) travels to atv via a piped
    # stdin, matched by a literal "-" in $AtvArgs on the caller's side --
    # never folded into argv. Discipline 3: $OutputEncoding (pinned above)
    # governs the encoding PowerShell uses when piping text into the child's
    # stdin. FAIL-1: every failure (atv missing, atv exits nonzero, the
    # process can't spawn) is swallowed here -- this hook must never
    # perturb the host session.
    param(
        [string[]]$AtvArgs,
        [AllowNull()][string]$StdinText
    )

    try {
        if ($script:AtvIsOverridden) {
            if ($null -ne $StdinText) {
                $StdinText | & $script:AtvStubExe @AtvArgs *> $null
            } else {
                & $script:AtvStubExe @AtvArgs *> $null
            }
        } else {
            if (-not (Get-Command atv -ErrorAction SilentlyContinue)) {
                return
            }
            if ($null -ne $StdinText) {
                $StdinText | & atv @AtvArgs *> $null
            } else {
                & atv @AtvArgs *> $null
            }
        }
    } catch {
        # Never disrupt the host session (FAIL-1). Swallow and continue.
    }
}

function Get-ToolSummary {
    # Returns a hashtable @{ Kind = <ERGO-31 kind>; Label = <label text or $null> }.
    # Falls back to the closed "tool" kind (docs/integration-api.md SS3) for any
    # tool name not in map.json's toolKind table, and falls back to the first
    # string-valued tool_input property if the tool's expected label field is
    # absent (defensive -- keeps an uncaptured/future tool from going label-less).
    param($ToolName, $ToolInput)

    $kind = $script:Map.toolKind.$ToolName
    if ([string]::IsNullOrEmpty($kind)) { $kind = "tool" }

    $label = $null
    $fieldName = $script:Map.toolLabelField.$ToolName
    if (-not [string]::IsNullOrEmpty($fieldName)) {
        $value = Get-Prop $ToolInput $fieldName
        if ($value -is [string]) { $label = $value }
    }
    if ([string]::IsNullOrEmpty($label) -and $null -ne $ToolInput) {
        foreach ($p in $ToolInput.PSObject.Properties) {
            if ($p.Value -is [string] -and -not [string]::IsNullOrEmpty($p.Value)) {
                $label = $p.Value
                break
            }
        }
    }
    if ([string]::IsNullOrEmpty($label)) {
        # Last-resort fallback (AC2's "an unmapped tool falls to --kind tool
        # --label <tool_name>"): no mapped field and no string-valued
        # tool_input property at all -- the tool name itself is still a
        # meaningful label rather than an empty one.
        $label = $ToolName
    }

    return @{ Kind = $kind; Label = $label }
}

function Get-PlanLabel {
    # The TodoWrite structural quirk (docs/integration-api.md SS3's "plan" kind):
    # AppTaskInfo has no numeric progress field, so a "(n/m) <item>" string is
    # composed here, in code, from the full todos array TodoWrite always
    # carries in tool_input. Picks the first in_progress item; if none is
    # in_progress (e.g. the very first or very last write), falls back to a
    # position derived from how many are already completed.
    param($ToolInput)

    $todos = Get-Prop $ToolInput "todos"
    if ($null -eq $todos) { return $null }
    $total = @($todos).Count
    if ($total -eq 0) { return $null }

    $activeIndex = -1
    for ($i = 0; $i -lt $total; $i++) {
        if ((Get-Prop $todos[$i] "status") -eq "in_progress") { $activeIndex = $i; break }
    }
    if ($activeIndex -lt 0) {
        $completedCount = 0
        foreach ($t in $todos) {
            if ((Get-Prop $t "status") -eq "completed") { $completedCount++ }
        }
        $activeIndex = [Math]::Min($completedCount, $total - 1)
    }

    $content = Get-Prop $todos[$activeIndex] "content"
    if ([string]::IsNullOrEmpty($content)) { return $null }

    $n = $activeIndex + 1
    return "($n/$total) $content"
}

# ---- load map.json (co-located with this script) --------------------------
$script:Map = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "map.json") | ConvertFrom-Json

# ---- discipline 3: read Claude Code's own payload as UTF-8, to EOF --------
$stdinReader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
try {
    $rawPayload = $stdinReader.ReadToEnd()
} finally {
    $stdinReader.Dispose()
}

try {
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($rawPayload)) {
        try { $payload = $rawPayload | ConvertFrom-Json } catch { $payload = $null }
    }

    if ($null -ne $payload) {
        $sid = Get-Prop $payload "session_id"

        if (-not [string]::IsNullOrEmpty($sid)) {
            switch ($Event) {

                "SessionStart" {
                    # ERGO-31 SS1's optional row: only "source":"compact" claims
                    # anything (a fixed-phrase activity kind); startup/resume/clear
                    # are a deliberate no-op -- there is no session-start verb.
                    $source = Get-Prop $payload "source"
                    if ($source -eq "compact") {
                        Invoke-Atv -AtvArgs (@("activity", $sid, "--kind", "compacting") + (Get-CwdArgs)) -StdinText $null
                    }
                }

                "UserPromptSubmit" {
                    $prompt = Get-Prop $payload "prompt"
                    if ($null -eq $prompt) { $prompt = "" }
                    # ERGO-33: this is the event that creates the card, so the
                    # host's session_title (present only when the user
                    # explicitly named this session) rides --title here and
                    # nowhere else -- no translator state needed.
                    $sessionTitle = Get-Prop $payload "session_title"
                    $argList = @("working", $sid, "--goal", "-") + (Get-TitleArgs $sessionTitle) + (Get-CwdArgs)
                    Invoke-Atv -AtvArgs $argList -StdinText $prompt
                }

                { $_ -in @("PreToolUse", "PostToolUse") } {
                    $toolName = Get-Prop $payload "tool_name"
                    $toolInput = Get-Prop $payload "tool_input"

                    if (-not [string]::IsNullOrEmpty($toolName) -and -not ($script:Map.suppressedTools -contains $toolName)) {
                        $agentId = Get-Prop $payload "agent_id"
                        $agentType = Get-Prop $payload "agent_type"
                        $extra = @()
                        if (-not [string]::IsNullOrEmpty($agentId)) { $extra += @("--agent", $agentId) }
                        if (-not [string]::IsNullOrEmpty($agentType)) { $extra += @("--name", $agentType) }

                        if ($toolName -eq $script:Map.planTool) {
                            $label = Get-PlanLabel -ToolInput $toolInput
                            if ($null -ne $label) {
                                $argList = @("activity", $sid, "--kind", "plan", "--label", "-") + $extra + (Get-CwdArgs)
                                Invoke-Atv -AtvArgs $argList -StdinText $label
                            }
                        } else {
                            $summary = Get-ToolSummary -ToolName $toolName -ToolInput $toolInput
                            $argList = @("activity", $sid, "--kind", $summary.Kind)
                            if ($summary.Kind -eq "tool") { $argList += @("--name", $toolName) }
                            $argList += @("--label", "-")
                            $argList += $extra
                            $argList += (Get-CwdArgs)
                            $labelText = $summary.Label
                            if ($null -eq $labelText) { $labelText = "" }
                            Invoke-Atv -AtvArgs $argList -StdinText $labelText
                        }
                    }
                }

                "PermissionRequest" {
                    # Attribution keys off PermissionRequest, NOT Notification
                    # (phase-14 capture finding 5: Notification:permission_prompt
                    # carries no agent_id; PermissionRequest does).
                    $toolName = Get-Prop $payload "tool_name"
                    $toolInput = Get-Prop $payload "tool_input"
                    $agentId = Get-Prop $payload "agent_id"

                    $summary = Get-ToolSummary -ToolName $toolName -ToolInput $toolInput
                    $question = $toolName
                    if ([string]::IsNullOrEmpty($question)) { $question = "Permission needed" }
                    if (-not [string]::IsNullOrEmpty($summary.Label)) { $question = "$($toolName): $($summary.Label)" }

                    $argList = @("blocked", $sid, "--question", "-")
                    if (-not [string]::IsNullOrEmpty($agentId)) { $argList += @("--agent", $agentId) }
                    $argList += (Get-CwdArgs)
                    Invoke-Atv -AtvArgs $argList -StdinText $question
                }

                "Notification" {
                    # Only idle_prompt claims anything -- permission_prompt is a
                    # deliberate no-op (PermissionRequest already owns Blocked);
                    # the matcher in hooks.json already restricts this hook line
                    # to idle_prompt, but the check is repeated here defensively.
                    $notificationType = Get-Prop $payload "notification_type"
                    if ($notificationType -eq "idle_prompt") {
                        Invoke-Atv -AtvArgs (@("ready", $sid) + (Get-CwdArgs)) -StdinText $null
                    }
                }

                "Stop" {
                    $summary = Get-Prop $payload "last_assistant_message"
                    if ($null -eq $summary) { $summary = "" }
                    Invoke-Atv -AtvArgs (@("ready", $sid, "--summary", "-") + (Get-CwdArgs)) -StdinText $summary
                }

                "StopFailure" {
                    # Never captured live (phase 14: "Not exercised -- requires an
                    # API error, not induced"). Best-effort field reading with a
                    # graceful fallback to the closed vocabulary's "fatal" catch-all
                    # per docs/integration-api.md SS4 -- flagged as an assumption in
                    # the phase-18 executor report.
                    $rawReason = Get-Prop $payload "reason"
                    if ([string]::IsNullOrEmpty($rawReason)) { $rawReason = Get-Prop $payload "error_type" }

                    $mapped = $null
                    if (-not [string]::IsNullOrEmpty($rawReason)) { $mapped = $script:Map.brokenReason.$rawReason }
                    if ([string]::IsNullOrEmpty($mapped)) { $mapped = $script:Map.defaultBrokenReason }

                    $detail = Get-Prop $payload "error"
                    if ([string]::IsNullOrEmpty($detail)) { $detail = Get-Prop $payload "message" }

                    $argList = @("broken", $sid, "--reason", $mapped) + (Get-CwdArgs)
                    if (-not [string]::IsNullOrEmpty($detail)) {
                        Invoke-Atv -AtvArgs ($argList + @("--detail", "-")) -StdinText $detail
                    } else {
                        Invoke-Atv -AtvArgs $argList -StdinText $null
                    }
                }

                "SubagentStart" {
                    $agentId = Get-Prop $payload "agent_id"
                    if (-not [string]::IsNullOrEmpty($agentId)) {
                        $agentType = Get-Prop $payload "agent_type"
                        $argList = @("agent-started", $sid, "--agent", $agentId)
                        if (-not [string]::IsNullOrEmpty($agentType)) { $argList += @("--name", $agentType) }
                        $argList += (Get-CwdArgs)
                        Invoke-Atv -AtvArgs $argList -StdinText $null
                    }
                }

                "SubagentStop" {
                    $agentId = Get-Prop $payload "agent_id"
                    if (-not [string]::IsNullOrEmpty($agentId)) {
                        Invoke-Atv -AtvArgs (@("agent-stopped", $sid, "--agent", $agentId) + (Get-CwdArgs)) -StdinText $null
                    }
                }

                "SessionEnd" {
                    # No identity flags, no upsert, no --cwd (SS2: the one verb that
                    # only acts on an already-live handle). reason field confirmed
                    # by the phase-14 capture (not exit_reason); both observed
                    # values (other / prompt_input_exit) map to "finished".
                    Invoke-Atv -AtvArgs @("session-ended", $sid, "--reason", $script:Map.sessionEndReasonVerb) -StdinText $null
                }

                default {
                    # Unmapped event -- no-op by design.
                }
            }
        }
    }
} catch {
    # FAIL-1: this hook must never surface a nonzero exit to Claude Code.
}

exit 0
