# Register the identity package for AppTaskInfoCli (dev/sideload, no signing required)
$appDir = (Resolve-Path ".\bin\Debug\net9.0-windows10.0.26100.0").Path
$manifest = (Resolve-Path ".\identity\AppxManifest.xml").Path

Write-Host "Registering identity package..."
Write-Host "  App dir: $appDir"
Write-Host "  Manifest: $manifest"

Add-AppxPackage -Register $manifest -ExternalLocation $appDir

Write-Host "Done. Run 'Get-AppxPackage AppTaskInfoCli' to verify."
