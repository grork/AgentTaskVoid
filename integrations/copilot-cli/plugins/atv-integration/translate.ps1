<#
.SYNOPSIS
    Translates one GitHub Copilot CLI hook payload into Agent Task Void's v2
    semantic verb surface.

.DESCRIPTION
    Host-specific logic lives here, never in atv.exe. The translator is
    stdout-silent, always exits 0, never passes --strict, and sends arbitrary
    display text to atv through stdin.

    Copilot 1.0.71 omits the parent/task identity from child-scoped hook
    payloads. This script bridges that host deficiency with short-lived,
    plugin-local correlation state:

      pending: SHA256(cwd + NUL + exact child prompt) -> parent + task
      active:  child call_* session id -> parent + task

    The pending record is atomically claimed by the child's first
    userPromptSubmitted event, then deleted. The active record is deleted on
    child completion. Raw prompts are never persisted. Zero or multiple
    pending matches fail open to lifecycle-only behavior; the translator never
    guesses and never disrupts Copilot.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Event
)

$ErrorActionPreference = "Stop"
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$script:AtvStubExe = $env:ATV_TRANSLATOR_STUB_EXE
$script:StateSchemaVersion = 1
$script:PendingTtl = [TimeSpan]::FromMinutes(10)
$script:ActiveTtl = [TimeSpan]::FromHours(24)
$script:Map = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "map.json") | ConvertFrom-Json

function Get-Prop {
    param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $null }
    $property = $Obj.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-StateRoot {
    if (-not [string]::IsNullOrEmpty($env:ATV_COPILOT_STATE_DIR)) {
        return $env:ATV_COPILOT_STATE_DIR
    }
    if (-not [string]::IsNullOrEmpty($env:COPILOT_PLUGIN_DATA)) {
        return $env:COPILOT_PLUGIN_DATA
    }
    if (-not [string]::IsNullOrEmpty($env:CLAUDE_PLUGIN_DATA)) {
        return $env:CLAUDE_PLUGIN_DATA
    }
    return $null
}

function Get-Sha256Hex {
    param([string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-PromptHash {
    param([string]$Cwd, [string]$Prompt)
    $cwdValue = if ($null -eq $Cwd) { "" } else { $Cwd }
    $promptValue = if ($null -eq $Prompt) { "" } else { $Prompt }
    return Get-Sha256Hex ($cwdValue + [char]0 + $promptValue)
}

function Write-Diagnostic {
    param([string]$Message)
    $root = Get-StateRoot
    if ([string]::IsNullOrEmpty($root)) { return }

    try {
        [void][System.IO.Directory]::CreateDirectory($root)
        $line = "{0} event={1} {2}{3}" -f [DateTimeOffset]::UtcNow.ToString("O"), $Event, $Message, [Environment]::NewLine
        [System.IO.File]::AppendAllText(
            (Join-Path $root "translator.log"),
            $line,
            (New-Object System.Text.UTF8Encoding($false)))
    }
    catch {
        # Diagnostics are best-effort; hook behavior must remain non-disruptive.
    }
}

# ---- atv command resolution -------------------------------------------------
# Production: the bare alias "atv" resolved via PATH. Two overrides sit above
# that, in priority order (DIST-12 SS4):
#   1. $env:ATV_TRANSLATOR_STUB_EXE -- the test seam, absolute priority. See
#      tests/Atv.LogicTests/Integrations/CopilotCliTranslatorTests.cs.
#   2. atv-command.txt in the state root Get-StateRoot resolves (beside
#      correlation-state.json/translator.log) -- a hand-written, single-line,
#      verbatim command override (typically the atv-dev shim's full path), for
#      pointing a working-tree dogfood at the dev pool instead of the
#      operator's daily retail atv install. No state root (Get-StateRoot
#      returns $null) means nowhere to read an override from, same as
#      correlation state already degrades. A present-but-broken target no-ops
#      (caught by Invoke-Atv's try/catch below) rather than falling back to
#      bare atv -- that fallback would leak a dev session's cards onto the
#      daily install. An empty/whitespace-only file counts as absent (falls
#      through to the guard below it).
$script:AtvCommand = $null
if (-not [string]::IsNullOrEmpty($script:AtvStubExe)) {
    $script:AtvCommand = $script:AtvStubExe
}
else {
    $atvCommandStateRoot = Get-StateRoot
    if (-not [string]::IsNullOrEmpty($atvCommandStateRoot)) {
        $atvCommandOverridePath = Join-Path $atvCommandStateRoot "atv-command.txt"
        if (Test-Path -LiteralPath $atvCommandOverridePath) {
            $atvCommandOverrideText = (Get-Content -Raw -LiteralPath $atvCommandOverridePath).Trim()
            if (-not [string]::IsNullOrEmpty($atvCommandOverrideText)) {
                $script:AtvCommand = $atvCommandOverrideText
                Write-Diagnostic ("atv-command.txt override in use: " + $script:AtvCommand)
            }
        }
    }
}
$script:AtvIsOverridden = ($null -ne $script:AtvCommand)

function New-CorrelationState {
    return [pscustomobject]@{
        schemaVersion = $script:StateSchemaVersion
        pending = @()
        active = @()
    }
}

function Read-CorrelationStateUnsafe {
    param([string]$Root)
    $path = Join-Path $Root "correlation-state.json"
    if (-not (Test-Path -LiteralPath $path)) {
        return New-CorrelationState
    }

    $parsed = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    return [pscustomobject]@{
        schemaVersion = $script:StateSchemaVersion
        pending = @(Get-Prop $parsed "pending")
        active = @(Get-Prop $parsed "active")
    }
}

function Write-CorrelationStateUnsafe {
    param([string]$Root, $State)
    [void][System.IO.Directory]::CreateDirectory($Root)

    $path = Join-Path $Root "correlation-state.json"
    $temp = "$path.$PID.$([Guid]::NewGuid().ToString('N')).tmp"
    $json = $State | ConvertTo-Json -Depth 12 -Compress
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($temp, $json, $encoding)

    try {
        if (Test-Path -LiteralPath $path) {
            $backup = "$path.$PID.$([Guid]::NewGuid().ToString('N')).bak"
            try {
                [System.IO.File]::Replace($temp, $path, $backup)
            }
            finally {
                if (Test-Path -LiteralPath $backup) {
                    [System.IO.File]::Delete($backup)
                }
            }
        }
        else {
            [System.IO.File]::Move($temp, $path)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            [System.IO.File]::Delete($temp)
        }
    }
}

function Remove-ExpiredStateUnsafe {
    param($State, [DateTimeOffset]$Now)
    $State.pending = @($State.pending | Where-Object {
        try { $Now - [DateTimeOffset]::Parse($_.createdAt) -le $script:PendingTtl }
        catch { $false }
    })
    $State.active = @($State.active | Where-Object {
        try { $Now - [DateTimeOffset]::Parse($_.createdAt) -le $script:ActiveTtl }
        catch { $false }
    })
}

function Invoke-CorrelationLocked {
    param([scriptblock]$Action)
    $root = Get-StateRoot
    if ([string]::IsNullOrEmpty($root)) { return $null }

    $mutex = $null
    $taken = $false
    try {
        [void][System.IO.Directory]::CreateDirectory($root)
        $normalized = [System.IO.Path]::GetFullPath($root).ToUpperInvariant()
        $mutexName = "Local\HostIntegration-" + (Get-Sha256Hex $normalized)
        $mutex = New-Object System.Threading.Mutex($false, $mutexName)
        try {
            $taken = $mutex.WaitOne([TimeSpan]::FromSeconds(5))
        }
        catch [System.Threading.AbandonedMutexException] {
            $taken = $true
        }

        if (-not $taken) {
            Write-Diagnostic "correlation lock timed out; degrading to lifecycle-only behavior"
            return $null
        }

        return & $Action $root
    }
    catch {
        Write-Diagnostic ("correlation operation failed: " + $_.Exception.Message)
        return $null
    }
    finally {
        if ($taken -and $null -ne $mutex) {
            try { $mutex.ReleaseMutex() } catch { }
        }
        if ($null -ne $mutex) { $mutex.Dispose() }
    }
}

function Save-PendingCorrelation {
    param(
        [string]$ParentSession,
        [string]$TaskName,
        [string]$AgentType,
        [string]$Mode,
        [string]$Cwd,
        [string]$Prompt
    )

    $hash = Get-PromptHash $Cwd $Prompt
    $record = [pscustomobject]@{
        promptHash = $hash
        parentSession = $ParentSession
        taskName = $TaskName
        agentType = $AgentType
        mode = $Mode
        cwd = $Cwd
        createdAt = [DateTimeOffset]::UtcNow.ToString("O")
    }

    $null = Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        Remove-ExpiredStateUnsafe $state ([DateTimeOffset]::UtcNow)
        $state.pending = @($state.pending | Where-Object {
            -not ($_.parentSession -eq $ParentSession -and $_.taskName -eq $TaskName)
        })
        $state.pending += $record
        Write-CorrelationStateUnsafe $root $state
    }
}

function Claim-PendingCorrelation {
    param([string]$ChildSession, [string]$Cwd, [string]$Prompt)
    $hash = Get-PromptHash $Cwd $Prompt

    $result = Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        Remove-ExpiredStateUnsafe $state ([DateTimeOffset]::UtcNow)

        $existing = @($state.active | Where-Object { $_.childSession -eq $ChildSession })
        if ($existing.Count -eq 1) {
            return [pscustomobject]@{ status = "existing"; record = $existing[0] }
        }

        $matches = @($state.pending | Where-Object {
            $_.promptHash -eq $hash -and $_.cwd -eq $Cwd
        })
        if ($matches.Count -ne 1) {
            Write-CorrelationStateUnsafe $root $state
            return [pscustomobject]@{ status = $(if ($matches.Count -eq 0) { "missing" } else { "ambiguous" }); record = $null }
        }

        $pending = $matches[0]
        $active = [pscustomobject]@{
            childSession = $ChildSession
            parentSession = $pending.parentSession
            taskName = $pending.taskName
            agentType = $pending.agentType
            mode = $pending.mode
            cwd = $pending.cwd
            createdAt = [DateTimeOffset]::UtcNow.ToString("O")
        }

        $state.pending = @($state.pending | Where-Object { $_ -ne $pending })
        $state.active = @($state.active | Where-Object { $_.childSession -ne $ChildSession })
        $state.active += $active
        Write-CorrelationStateUnsafe $root $state
        return [pscustomobject]@{ status = "claimed"; record = $active }
    }

    if ($null -ne $result -and $result.status -in @("missing", "ambiguous")) {
        Write-Diagnostic ("child correlation " + $result.status + " for session " + $ChildSession)
    }
    return $result
}

function Get-ActiveCorrelation {
    param([string]$ChildSession)
    return Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        Remove-ExpiredStateUnsafe $state ([DateTimeOffset]::UtcNow)
        $matches = @($state.active | Where-Object { $_.childSession -eq $ChildSession })
        if ($matches.Count -eq 1) { return $matches[0] }
        return $null
    }
}

function Complete-CorrelationByChild {
    param([string]$ChildSession)
    return Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        Remove-ExpiredStateUnsafe $state ([DateTimeOffset]::UtcNow)
        $matches = @($state.active | Where-Object { $_.childSession -eq $ChildSession })
        $record = if ($matches.Count -eq 1) { $matches[0] } else { $null }
        $state.active = @($state.active | Where-Object { $_.childSession -ne $ChildSession })
        Write-CorrelationStateUnsafe $root $state
        return $record
    }
}

function Complete-CorrelationByTask {
    param([string]$ParentSession, [string]$TaskName)
    return Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        Remove-ExpiredStateUnsafe $state ([DateTimeOffset]::UtcNow)
        $pendingCount = @($state.pending).Count
        $activeCount = @($state.active).Count
        $state.pending = @($state.pending | Where-Object {
            -not ($_.parentSession -eq $ParentSession -and $_.taskName -eq $TaskName)
        })
        $state.active = @($state.active | Where-Object {
            -not ($_.parentSession -eq $ParentSession -and $_.taskName -eq $TaskName)
        })
        Write-CorrelationStateUnsafe $root $state
        return $pendingCount -ne @($state.pending).Count -or $activeCount -ne @($state.active).Count
    }
}

function Clear-CorrelationsForParent {
    param([string]$ParentSession)
    $null = Invoke-CorrelationLocked {
        param($root)
        $state = Read-CorrelationStateUnsafe $root
        $state.pending = @($state.pending | Where-Object { $_.parentSession -ne $ParentSession })
        $state.active = @($state.active | Where-Object { $_.parentSession -ne $ParentSession })
        Write-CorrelationStateUnsafe $root $state
    }
}

function Convert-ToolArgs {
    param($Raw)
    if ($null -eq $Raw) { return $null }
    if ($Raw -isnot [string]) { return $Raw }
    try { return $Raw | ConvertFrom-Json } catch { return $Raw }
}

function Get-MapValue {
    param($Table, [string]$Key)
    $property = $Table.PSObject.Properties[$Key]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-ToolSummary {
    param([string]$ToolName, $RawToolArgs)
    $toolArgs = Convert-ToolArgs $RawToolArgs
    $kind = Get-MapValue $script:Map.toolKind $ToolName
    if ([string]::IsNullOrEmpty($kind)) { $kind = "tool" }

    $label = $null
    $fieldName = Get-MapValue $script:Map.toolLabelField $ToolName
    if (-not [string]::IsNullOrEmpty($fieldName) -and $toolArgs -isnot [string]) {
        $candidate = Get-Prop $toolArgs $fieldName
        if ($candidate -is [string]) { $label = $candidate }
    }

    if ([string]::IsNullOrEmpty($label) -and $ToolName -eq "apply_patch" -and $RawToolArgs -is [string]) {
        if ($RawToolArgs -match "(?m)^\*\*\* (?:Add|Update|Delete) File: (.+)$") {
            $label = $Matches[1]
        }
    }

    if ([string]::IsNullOrEmpty($label) -and $toolArgs -isnot [string] -and $null -ne $toolArgs) {
        foreach ($property in $toolArgs.PSObject.Properties) {
            if ($property.Value -is [string] -and -not [string]::IsNullOrEmpty($property.Value)) {
                $label = $property.Value
                break
            }
        }
    }
    if ([string]::IsNullOrEmpty($label)) { $label = $ToolName }

    return [pscustomobject]@{ kind = $kind; label = $label }
}

function Get-CwdArgs {
    param([string]$Cwd)
    if ([string]::IsNullOrEmpty($Cwd)) { return @() }
    return @("--cwd", $Cwd)
}

function Invoke-Atv {
    param([string[]]$AtvArgs, [AllowNull()][string]$StdinText)
    try {
        if ($script:AtvIsOverridden) {
            if ($null -ne $StdinText) {
                $StdinText | & $script:AtvCommand @AtvArgs *> $null
            }
            else {
                & $script:AtvCommand @AtvArgs *> $null
            }
        }
        else {
            if (-not (Get-Command atv -ErrorAction SilentlyContinue)) { return }
            if ($null -ne $StdinText) {
                $StdinText | & atv @AtvArgs *> $null
            }
            else {
                & atv @AtvArgs *> $null
            }
        }
    }
    catch {
        Write-Diagnostic ("atv invocation failed: " + $_.Exception.Message)
    }
}

function Resolve-Target {
    param([string]$SessionId)
    if (-not $SessionId.StartsWith("call_", [StringComparison]::Ordinal)) {
        return [pscustomobject]@{ handle = $SessionId; agent = $null; correlation = $null }
    }

    $correlation = Get-ActiveCorrelation $SessionId
    if ($null -ne $correlation) {
        return [pscustomobject]@{
            handle = $correlation.parentSession
            agent = $correlation.taskName
            correlation = $correlation
        }
    }
    return $null
}

function Add-AgentArgs {
    param([string[]]$BaseArgs, [AllowNull()][string]$Agent)
    if (-not [string]::IsNullOrEmpty($Agent)) {
        return $BaseArgs + @("--agent", $Agent)
    }
    return $BaseArgs
}

function Complete-Agent {
    param([string]$ParentSession, [string]$TaskName, [string]$Cwd, [switch]$EmitReady)
    $stateChanged = Complete-CorrelationByTask $ParentSession $TaskName
    if ($stateChanged -is [bool] -and -not $stateChanged) {
        return
    }
    Invoke-Atv -AtvArgs (@("agent-stopped", $ParentSession, "--agent", $TaskName) + (Get-CwdArgs $Cwd)) -StdinText $null
    # ready is a PARENT turn-end signal, and atv refuses it only while other
    # active agent loci remain -- so with a single subagent it always lands.
    # Emit it only when the parent genuinely has nothing left to do: a
    # background worker finishing after the parent turn already ended
    # (notification path). A synchronous subagent's completion instead RESUMES
    # the parent turn -- the parent posts the agent's results and keeps
    # working -- so emitting ready here flips the card to Completed mid-turn and
    # it only recovers on the next parent event. The parent's own agentStop
    # supplies ready at the true turn end.
    if ($EmitReady) {
        Invoke-Atv -AtvArgs (@("ready", $ParentSession) + (Get-CwdArgs $Cwd)) -StdinText $null
    }
}

function Get-NotificationAgentName {
    param($Payload)
    $message = [string](Get-Prop $Payload "message")
    if ($message -match '^Agent "([^"]+)"') { return $Matches[1] }

    $title = [string](Get-Prop $Payload "title")
    if ($title -match '^Agent (.+?) (?:idle|completed|failed)$') { return $Matches[1] }
    return $null
}

function Get-BrokenReason {
    param([string]$Text)
    $value = if ($null -eq $Text) { "" } else { $Text }
    $lower = $value.ToLowerInvariant()
    if ($lower.Contains("rate") -and $lower.Contains("limit")) { return "rate-limit" }
    if ($lower.Contains("overload")) { return "overloaded" }
    if ($lower.Contains("timeout") -or $lower.Contains("timed out")) { return "timeout" }
    if ($lower.Contains("api") -or $lower.Contains("model")) { return "api-error" }
    return "fatal"
}

$stdinReader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
try {
    $rawPayload = $stdinReader.ReadToEnd()
}
finally {
    $stdinReader.Dispose()
}

try {
    if ([string]::IsNullOrWhiteSpace($rawPayload)) { exit 0 }
    try { $payload = $rawPayload | ConvertFrom-Json } catch { exit 0 }

    $sessionId = [string](Get-Prop $payload "sessionId")
    $cwd = [string](Get-Prop $payload "cwd")
    if ([string]::IsNullOrEmpty($sessionId)) { exit 0 }

    switch ($Event) {
        "userPromptSubmitted" {
            $prompt = [string](Get-Prop $payload "prompt")
            if ($sessionId.StartsWith("call_", [StringComparison]::Ordinal)) {
                $null = Claim-PendingCorrelation $sessionId $cwd $prompt
            }
            elseif (-not $prompt.TrimStart().StartsWith("<system_notification>", [StringComparison]::Ordinal)) {
                Invoke-Atv -AtvArgs (@("working", $sessionId, "--goal", "-") + (Get-CwdArgs $cwd)) -StdinText $prompt
            }
        }

        "preToolUse" {
            $toolName = [string](Get-Prop $payload "toolName")
            $rawToolArgs = Get-Prop $payload "toolArgs"
            $toolArgs = Convert-ToolArgs $rawToolArgs
            $target = Resolve-Target $sessionId
            if ($null -eq $target) { break }

            if ($toolName -eq "task" -and $toolArgs -isnot [string] -and $null -ne $toolArgs) {
                $taskName = [string](Get-Prop $toolArgs "name")
                $prompt = [string](Get-Prop $toolArgs "prompt")
                $agentType = [string](Get-Prop $toolArgs "agent_type")
                $mode = [string](Get-Prop $toolArgs "mode")
                if (-not [string]::IsNullOrEmpty($taskName) -and -not [string]::IsNullOrEmpty($prompt)) {
                    $displayName = if (-not [string]::IsNullOrEmpty($agentType)) { $agentType } else { $taskName }
                    Save-PendingCorrelation $target.handle $taskName $agentType $mode $cwd $prompt
                    Invoke-Atv -AtvArgs (@("agent-started", $target.handle, "--agent", $taskName, "--name", $displayName) + (Get-CwdArgs $cwd)) -StdinText $null
                }
                break
            }

            if ($toolName -eq "ask_user") {
                $question = if ($toolArgs -isnot [string]) { [string](Get-Prop $toolArgs "question") } else { "Input needed" }
                if ([string]::IsNullOrEmpty($question)) { $question = "Input needed" }
                $args = Add-AgentArgs -BaseArgs @("blocked", $target.handle, "--question", "-") -Agent $target.agent
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $question
                break
            }

            $summary = Get-ToolSummary $toolName $rawToolArgs
            $args = @("activity", $target.handle, "--kind", $summary.kind, "--label", "-")
            if ($summary.kind -eq "tool") { $args += @("--name", $toolName) }
            $args = Add-AgentArgs -BaseArgs $args -Agent $target.agent
            Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $summary.label
        }

        "postToolUse" {
            $toolName = [string](Get-Prop $payload "toolName")
            $toolArgs = Convert-ToolArgs (Get-Prop $payload "toolArgs")
            $target = Resolve-Target $sessionId
            if ($null -eq $target) { break }

            if ($toolName -eq "task" -and $toolArgs -isnot [string] -and $null -ne $toolArgs) {
                $taskName = [string](Get-Prop $toolArgs "name")
                $mode = [string](Get-Prop $toolArgs "mode")
                if (-not [string]::IsNullOrEmpty($taskName) -and $mode -ne "background") {
                    Complete-Agent $target.handle $taskName $cwd
                }
                break
            }

            if ($toolName -eq "ask_user") {
                $args = Add-AgentArgs -BaseArgs @("activity", $target.handle, "--kind", "tool", "--name", "ask_user", "--label", "-") -Agent $target.agent
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText "Question answered"
            }
        }

        "notification" {
            $notificationType = [string](Get-Prop $payload "notification_type")
            if ($notificationType -in @("agent_idle", "agent_completed")) {
                $taskName = Get-NotificationAgentName $payload
                if (-not [string]::IsNullOrEmpty($taskName)) {
                    Complete-Agent $sessionId $taskName $cwd -EmitReady
                }
                break
            }

            $target = Resolve-Target $sessionId
            if ($null -eq $target) { break }
            $message = [string](Get-Prop $payload "message")
            if ([string]::IsNullOrEmpty($message)) { $message = [string](Get-Prop $payload "title") }

            if ($notificationType -in @("permission_prompt", "elicitation_dialog")) {
                $args = Add-AgentArgs -BaseArgs @("blocked", $target.handle, "--question", "-") -Agent $target.agent
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $message
            }
            elseif ($notificationType -in @("shell_completed", "shell_detached_completed")) {
                $args = Add-AgentArgs -BaseArgs @("activity", $target.handle, "--kind", "shell", "--label", "-") -Agent $target.agent
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $message
            }
        }

        "agentStop" {
            if ($sessionId.StartsWith("call_", [StringComparison]::Ordinal)) {
                $correlation = Complete-CorrelationByChild $sessionId
                if ($null -ne $correlation) {
                    Invoke-Atv -AtvArgs (@("agent-stopped", $correlation.parentSession, "--agent", $correlation.taskName) + (Get-CwdArgs $cwd)) -StdinText $null
                    # Only a background worker's stop is a parent turn-end signal
                    # (the parent turn already ended when it launched the
                    # worker). A synchronous subagent's stop resumes the parent
                    # turn, so ready is left to the parent's own agentStop --
                    # otherwise a single sync subagent flips the parent card to
                    # Completed while it is still posting the agent's results.
                    if ($correlation.mode -eq "background") {
                        Invoke-Atv -AtvArgs (@("ready", $correlation.parentSession) + (Get-CwdArgs $cwd)) -StdinText $null
                    }
                }
            }
            else {
                Invoke-Atv -AtvArgs (@("ready", $sessionId) + (Get-CwdArgs $cwd)) -StdinText $null
            }
        }

        "preCompact" {
            $target = Resolve-Target $sessionId
            if ($null -ne $target) {
                $args = Add-AgentArgs -BaseArgs @("activity", $target.handle, "--kind", "compacting") -Agent $target.agent
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $null
            }
        }

        "errorOccurred" {
            $recoverable = [bool](Get-Prop $payload "recoverable")
            if ($recoverable) { break }

            $target = Resolve-Target $sessionId
            if ($null -eq $target) { break }
            $errorObject = Get-Prop $payload "error"
            $message = [string](Get-Prop $errorObject "message")
            $name = [string](Get-Prop $errorObject "name")
            $detail = (($name, $message) | Where-Object { -not [string]::IsNullOrEmpty($_) }) -join ": "
            $reason = Get-BrokenReason $detail
            $args = @("broken", $target.handle, "--reason", $reason)
            if (-not [string]::IsNullOrEmpty($detail)) {
                Invoke-Atv -AtvArgs ($args + @("--detail", "-") + (Get-CwdArgs $cwd)) -StdinText $detail
            }
            else {
                Invoke-Atv -AtvArgs ($args + (Get-CwdArgs $cwd)) -StdinText $null
            }
        }

        "sessionEnd" {
            if ($sessionId.StartsWith("call_", [StringComparison]::Ordinal)) {
                $correlation = Complete-CorrelationByChild $sessionId
                if ($null -ne $correlation) {
                    Invoke-Atv -AtvArgs (@("agent-stopped", $correlation.parentSession, "--agent", $correlation.taskName) + (Get-CwdArgs $cwd)) -StdinText $null
                }
                break
            }
            $reason = [string](Get-Prop $payload "reason")
            if ($reason -in @("user_exit", "abort")) {
                Clear-CorrelationsForParent $sessionId
                Invoke-Atv -AtvArgs @("session-ended", $sessionId, "--reason", "finished") -StdinText $null
            }
            elseif ($reason -in @("error", "timeout")) {
                Clear-CorrelationsForParent $sessionId
                Invoke-Atv -AtvArgs @("session-ended", $sessionId, "--reason", "error") -StdinText $null
            }
            elseif ($reason -eq "complete") {
                # Prompt mode may emit an early complete while background agents
                # are still active, then another complete later. Ready is safe:
                # the engine refuses it while active loci remain.
                Invoke-Atv -AtvArgs (@("ready", $sessionId) + (Get-CwdArgs $cwd)) -StdinText $null
            }
        }
    }
}
catch {
    Write-Diagnostic ("unhandled translator failure: " + $_.Exception.Message)
}

exit 0
