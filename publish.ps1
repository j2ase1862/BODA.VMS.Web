# BODA.VMS.Web - Publish & Deploy Script
# Usage:
#   .\publish.ps1                    # 빌드 + 게시만
#   .\publish.ps1 -Install           # 게시 후 Windows 서비스 설치
#   .\publish.ps1 -Uninstall         # Windows 서비스 제거
#   .\publish.ps1 -Deploy -RemotePath "\\SERVER-B\C$\BodaVMS"   # 빌드 + 서버 B에 배포
#
# 관리자 권한 필요: -Install / -Uninstall / -Deploy 사용 시

param(
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$Deploy,
    [string]$RemotePath,
    [string]$RemoteServiceName = "BodaVmsWeb",
    [string]$RemoteComputer,
    [string]$ServiceName = "BodaVmsWeb",
    [string]$PublishDir = ".\publish"
)

$ErrorActionPreference = "Stop"

$projectPath = "BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj"
$exeName = "BODA.VMS.Web.exe"

# ── Uninstall ────────────────────────────────────────────
if ($Uninstall) {
    Write-Host "[*] Stopping service '$ServiceName'..." -ForegroundColor Yellow
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq "Running") {
            Stop-Service -Name $ServiceName -Force
            Write-Host "    Service stopped." -ForegroundColor Green
        }
        sc.exe delete $ServiceName | Out-Null
        Write-Host "    Service '$ServiceName' removed." -ForegroundColor Green
    } else {
        Write-Host "    Service '$ServiceName' not found." -ForegroundColor Gray
    }
    exit 0
}

# ── Publish ──────────────────────────────────────────────
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  BODA.VMS.Web - Publish" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
Write-Host "[1/3] Publishing to: $resolvedPublishDir" -ForegroundColor Yellow

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $resolvedPublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host "[2/3] Publish complete." -ForegroundColor Green

# Verify output
$exePath = Join-Path $resolvedPublishDir $exeName
if (!(Test-Path $exePath)) {
    Write-Host "ERROR: $exeName not found in publish output!" -ForegroundColor Red
    exit 1
}

$fileSize = (Get-Item $exePath).Length / 1MB
Write-Host "[3/3] Output: $exePath ($([math]::Round($fileSize, 1)) MB)" -ForegroundColor Green

# ── Install as Windows Service ───────────────────────────
if ($Install) {
    Write-Host ""
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Installing Windows Service" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host ""

    # Stop existing service if running
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "[*] Stopping existing service..." -ForegroundColor Yellow
        if ($svc.Status -eq "Running") {
            Stop-Service -Name $ServiceName -Force
        }
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    # Create service
    Write-Host "[*] Creating service '$ServiceName'..." -ForegroundColor Yellow
    sc.exe create $ServiceName `
        binPath= "`"$exePath`" --environment Production" `
        start= auto `
        DisplayName= "BODA VMS Web Server" | Out-Null

    # Set description
    sc.exe description $ServiceName "BODA Vision Management System - Web Server" | Out-Null

    # Set recovery: restart on failure (1st: 10s, 2nd: 30s, 3rd: 60s)
    sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null

    Write-Host "[*] Starting service..." -ForegroundColor Yellow
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $svc = Get-Service -Name $ServiceName
    if ($svc.Status -eq "Running") {
        Write-Host ""
        Write-Host "Service '$ServiceName' is running!" -ForegroundColor Green
        Write-Host "URL: http://localhost:5292" -ForegroundColor Cyan
    } else {
        Write-Host "WARNING: Service status is '$($svc.Status)'" -ForegroundColor Yellow
    }
}

# ── Deploy to Remote Server ────────────────────────────────
if ($Deploy) {
    if (-not $RemotePath) {
        Write-Host "ERROR: -RemotePath is required for deploy." -ForegroundColor Red
        Write-Host "  Example: .\publish.ps1 -Deploy -RemotePath '\\SERVER-B\C$\BodaVMS'" -ForegroundColor Gray
        Write-Host "  Example: .\publish.ps1 -Deploy -RemotePath '\\SERVER-B\C$\BodaVMS' -RemoteComputer SERVER-B" -ForegroundColor Gray
        exit 1
    }

    Write-Host ""
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Deploy to Remote Server" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host ""

    # Auto-detect remote computer name from UNC path if not specified
    if (-not $RemoteComputer -and $RemotePath -match '^\\\\([^\\]+)\\') {
        $RemoteComputer = $Matches[1]
        Write-Host "[*] Detected remote computer: $RemoteComputer" -ForegroundColor Gray
    }

    # Stop remote service
    if ($RemoteComputer) {
        Write-Host "[1/4] Stopping remote service '$RemoteServiceName' on $RemoteComputer..." -ForegroundColor Yellow
        $remoteSvc = Get-Service -ComputerName $RemoteComputer -Name $RemoteServiceName -ErrorAction SilentlyContinue
        if ($remoteSvc -and $remoteSvc.Status -eq "Running") {
            Stop-Service -InputObject $remoteSvc -Force
            Start-Sleep -Seconds 2
            Write-Host "    Service stopped." -ForegroundColor Green
        } else {
            Write-Host "    Service not running or not found (will copy files anyway)." -ForegroundColor Gray
        }
    } else {
        Write-Host "[1/4] No -RemoteComputer specified, skipping service stop." -ForegroundColor Gray
        Write-Host "    Make sure the remote service is stopped before deploying!" -ForegroundColor Yellow
    }

    # Copy files
    Write-Host "[2/4] Copying files to $RemotePath..." -ForegroundColor Yellow
    if (!(Test-Path $RemotePath)) {
        New-Item -ItemType Directory -Path $RemotePath -Force | Out-Null
    }
    robocopy $resolvedPublishDir $RemotePath /MIR /NJH /NJS /NDL /NFL /NC /NS /NP /XF "appsettings.Production.json" | Out-Null
    Write-Host "    Files copied (appsettings.Production.json preserved)." -ForegroundColor Green

    # Start remote service
    if ($RemoteComputer) {
        # Install service if not exists
        $remoteSvc = Get-Service -ComputerName $RemoteComputer -Name $RemoteServiceName -ErrorAction SilentlyContinue
        if (-not $remoteSvc) {
            Write-Host "[3/4] Installing service on $RemoteComputer..." -ForegroundColor Yellow
            # Convert UNC path to local path on remote (\\SERVER\C$\BodaVMS → C:\BodaVMS)
            $remoteLocalPath = $RemotePath -replace "^\\\\[^\\]+\\([A-Za-z])\\\$", '$1:'
            $remoteExe = Join-Path $remoteLocalPath $exeName
            sc.exe \\$RemoteComputer create $RemoteServiceName `
                binPath= "`"$remoteExe`" --environment Production" `
                start= auto `
                DisplayName= "BODA VMS Web Server" | Out-Null
            sc.exe \\$RemoteComputer description $RemoteServiceName "BODA Vision Management System - Web Server" | Out-Null
            sc.exe \\$RemoteComputer failure $RemoteServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
            Write-Host "    Service installed." -ForegroundColor Green
        } else {
            Write-Host "[3/4] Service already exists." -ForegroundColor Gray
        }

        Write-Host "[4/4] Starting remote service..." -ForegroundColor Yellow
        $remoteSvc = Get-Service -ComputerName $RemoteComputer -Name $RemoteServiceName
        Start-Service -InputObject $remoteSvc
        Start-Sleep -Seconds 2
        $remoteSvc.Refresh()
        if ($remoteSvc.Status -eq "Running") {
            Write-Host ""
            Write-Host "Deploy complete! Service is running on $RemoteComputer." -ForegroundColor Green
        } else {
            Write-Host "WARNING: Service status is '$($remoteSvc.Status)'" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[3/4] Skipped (no -RemoteComputer)." -ForegroundColor Gray
        Write-Host "[4/4] Skipped (no -RemoteComputer)." -ForegroundColor Gray
        Write-Host ""
        Write-Host "Files deployed. Start the service manually on the remote server." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
