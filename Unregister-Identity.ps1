# Unregister the identity package for AppTaskInfoCli
Get-AppxPackage AppTaskInfoCli | Remove-AppxPackage
Write-Host "Unregistered."
