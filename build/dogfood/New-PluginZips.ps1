<#
.SYNOPSIS
    Zips the git-tracked working-tree contents of each integrations/<host>/
    directory into artifacts/dogfood/<ZipPrefix>-<host>.zip.

.DESCRIPTION
    Build-time helper for build/Atv.Dogfood.targets. Enumerates
    integrations/* automatically -- no per-host special-casing. For each
    host directory, `git ls-files` supplies the file list (working-tree
    content of tracked files, so uncommitted edits ship but gitignored files
    never do -- this is what keeps a working-tree
    integrations/claude-code/plugins/atv-integration/atv-command.txt
    override out of a recipient's plugin). Files are staged under a temp
    directory with the integrations/<host>/ prefix stripped, so the zip's
    root is the host directory's own content (a `claude plugin marketplace
    add <extracted-dir>` target).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RepoRoot,

    [Parameter(Mandatory)]
    [string] $OutputDir,

    [Parameter(Mandatory)]
    [string] $ZipPrefix
)

$ErrorActionPreference = 'Stop'

$integrationsRoot = Join-Path $RepoRoot 'integrations'
if (-not (Test-Path $integrationsRoot)) {
    throw "[Atv.Dogfood] Integrations root not found: $integrationsRoot"
}

$hostDirs = Get-ChildItem -Path $integrationsRoot -Directory | Sort-Object Name
if (-not $hostDirs) {
    throw "[Atv.Dogfood] No integrations/<host> directories found under $integrationsRoot"
}

foreach ($hostDir in $hostDirs) {
    $hostName = $hostDir.Name
    $subtree = "integrations/$hostName"

    $tracked = & git -C $RepoRoot ls-files -- $subtree
    if (-not $tracked) {
        Write-Warning "[Atv.Dogfood] No git-tracked files under $subtree -- skipping zip."
        continue
    }

    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("$ZipPrefix-stage-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null

    try {
        foreach ($relPath in $tracked) {
            $srcPath = Join-Path $RepoRoot ($relPath -replace '/', '\')
            $withinHost = ($relPath.Substring($subtree.Length + 1)) -replace '/', '\'
            $destPath = Join-Path $stage $withinHost
            $destDir = Split-Path $destPath -Parent
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            Copy-Item -LiteralPath $srcPath -Destination $destPath -Force
        }

        $zipPath = Join-Path $OutputDir "$ZipPrefix-$hostName.zip"
        if (Test-Path $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force

        Write-Host "[Atv.Dogfood] Zipped $subtree -> $zipPath ($($tracked.Count) files)"
    }
    finally {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
