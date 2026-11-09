<#
.SYNOPSIS
  Stops and removes the two Classic Mobile services and their firewall rules. The program files are not deleted. Run as Administrator.
#>
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this as Administrator (right-click Uninstall-Services.cmd -> Run as administrator).'
}
foreach ($name in 'ClassicMobileAPI1', 'ClassicMobileAPI2') {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
        sc.exe delete $name | Out-Null
        Write-Host "removed service $name" -ForegroundColor Green
    } else { Write-Host "service $name is not installed" }
    Get-NetFirewallRule -DisplayName "Classic Mobile $name port *" -ErrorAction SilentlyContinue | ForEach-Object { Remove-NetFirewallRule -DisplayName $_.DisplayName; Write-Host "removed firewall rule $($_.DisplayName)" }
}
Write-Host 'Done.'
