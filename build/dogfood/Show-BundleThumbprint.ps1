<#
.SYNOPSIS
    Prints the Authenticode signing thumbprint of a just-built dogfood bundle.

.DESCRIPTION
    Build-time helper for build/Atv.Dogfood.targets. Reads the signer straight
    from the bundle's own Authenticode signature -- the same call the shipped
    install.ps1 makes -- so the operator can quote this thumbprint out of band
    and a recipient can compare it against what the installer displays. Never
    reads a certificate store, never touches machine state.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BundlePath
)

$ErrorActionPreference = 'Stop'

$sig = Get-AuthenticodeSignature -FilePath $BundlePath
if ($sig.Status -eq 'NotSigned' -or $null -eq $sig.SignerCertificate) {
    throw "[Atv.Dogfood] '$BundlePath' has no readable signature (Status=$($sig.Status))."
}

$signer = $sig.SignerCertificate
Write-Host "[Atv.Dogfood] Bundle signed: $BundlePath"
Write-Host "[Atv.Dogfood] Signer subject:  $($signer.Subject)"
Write-Host "[Atv.Dogfood] Signer thumbprint: $($signer.Thumbprint)"
Write-Host "[Atv.Dogfood] Signer expires: $($signer.NotAfter)"
