<#
.SYNOPSIS
  Installs API 1 and API 2 as Windows services (start automatically with the server, restart themselves if they stop).

.DESCRIPTION
  Reads each program's settings and port from its start script in this folder (Start-API1-Gateway.cmd, Start-API2-DataApi.cmd), so the
  passwords and keys stay in ONE place. Creates the services, stores their settings for the service only, opens the Windows firewall for
  the two ports, starts them and checks /health.  Run it as Administrator (Install-Services.cmd does that for you).
  Safe to run again: an existing service of the same name is replaced.

  -DryRun   shows what would be done (setting VALUES are never printed) and changes nothing; needs no administrator rights.
#>
param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$services = @(
    @{ Name = 'ClassicMobileAPI1'; Display = 'Classic Mobile API 1 (Gateway)'; Exe = "$root\Gateway\Mobile.Gateway.exe"; EnvFile = "$root\Start-API1-Gateway.cmd";
       Text = 'Classic ERP mobile API 1: signs the apps in and gives them their sealed configuration.' },
    @{ Name = 'ClassicMobileAPI2'; Display = 'Classic Mobile API 2 (Data)'; Exe = "$root\DataApi\Mobile.DataApi.exe"; EnvFile = "$root\Start-API2-DataApi.cmd";
       Text = 'Classic ERP mobile API 2: serves the master data from the client database.' }
)

function Read-Settings($file) {
    $env = @(); $port = $null
    foreach ($line in Get-Content $file) {
        if ($line -match '^\s*set\s+([A-Za-z0-9_]+)=(.*)$') { $env += [pscustomobject]@{ Name = $Matches[1]; Value = $Matches[2].TrimEnd() } }
        if ($line -match '--urls\s+http://[^:\s]+:(\d+)') { $port = [int]$Matches[1] }
    }
    [pscustomobject]@{ Env = $env; Port = $port }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $DryRun -and -not $isAdmin) { throw 'Run this as Administrator (right-click Install-Services.cmd -> Run as administrator).' }
if ($DryRun) { Write-Host '*** DRY RUN: nothing is changed, setting values are hidden ***' -ForegroundColor Yellow }

foreach ($s in $services) {
    Write-Host ''; Write-Host "== $($s.Display)" -ForegroundColor Cyan
    if (-not (Test-Path $s.Exe)) { throw "Program not found: $($s.Exe)" }
    if (-not (Test-Path $s.EnvFile)) { throw "Settings file not found: $($s.EnvFile)" }
    $cfg = Read-Settings $s.EnvFile
    if (-not $cfg.Port) { throw "No port (--urls http://0.0.0.0:PORT) found in $($s.EnvFile)" }
    $bad = $cfg.Env | Where-Object { $_.Value -eq '' -or $_.Value.StartsWith('<') }
    if ($bad) { throw "Fill in these settings in $(Split-Path $s.EnvFile -Leaf) first: $(($bad.Name) -join ', ')" }
    $bin = "`"$($s.Exe)`" --urls http://0.0.0.0:$($cfg.Port)"
    $fw = "Classic Mobile $($s.Name) port $($cfg.Port)"

    Write-Host "   service name : $($s.Name)  (automatic start, restarts itself on failure)"
    Write-Host "   program      : $bin"
    Write-Host "   port         : $($cfg.Port)  (firewall rule: $fw)"
    Write-Host "   settings     : $(($cfg.Env.Name) -join ', ')"
    if ($DryRun) { continue }

    # replace an existing service of the same name
    if (Get-Service -Name $s.Name -ErrorAction SilentlyContinue) {
        Write-Host '   replacing the existing service...'
        Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue
        sc.exe delete $s.Name | Out-Null
        1..20 | ForEach-Object { if (Get-Service -Name $s.Name -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 } }
    }
    New-Service -Name $s.Name -BinaryPathName $bin -DisplayName $s.Display -Description $s.Text -StartupType Automatic | Out-Null
    # the settings belong to the service only (not the whole machine)
    $values = @($cfg.Env | ForEach-Object { "$($_.Name)=$($_.Value)" })
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)" -Name Environment -Value $values -Type MultiString
    sc.exe failure $s.Name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

    if (Get-NetFirewallRule -DisplayName $fw -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -DisplayName $fw }
    New-NetFirewallRule -DisplayName $fw -Direction Inbound -Protocol TCP -LocalPort $cfg.Port -Action Allow | Out-Null

    Start-Service -Name $s.Name
    $ok = $false
    1..30 | ForEach-Object {
        if (-not $ok) { Start-Sleep -Seconds 1; try { $ok = ((Invoke-WebRequest "http://localhost:$($cfg.Port)/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) } catch { } }
    }
    if ($ok) { Write-Host "   RUNNING - http://localhost:$($cfg.Port)/health answers ok" -ForegroundColor Green }
    else { Write-Host "   The service was created but /health did not answer in 30 s. Check: Get-EventLog -LogName Application -Newest 10 -Source '.NET Runtime'" -ForegroundColor Red }
}

Write-Host ''
if ($DryRun) { Write-Host 'Dry run finished. Run Install-Services.cmd to install for real.' -ForegroundColor Yellow }
else {
    Write-Host 'Done. Both services start automatically when the server starts.' -ForegroundColor Green
    Write-Host 'Manage them in Services (services.msc): "Classic Mobile API 1 (Gateway)" and "Classic Mobile API 2 (Data)".'
    Write-Host 'The start scripts (*.cmd) still hold the passwords and keys: keep them private (or move them off the server) once the services are installed.'
}
