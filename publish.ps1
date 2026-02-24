# BODA.VMS.Web - Publish & Deploy Script
# Usage:
#   .\publish.ps1                    # 빌드 + 게시만
#   .\publish.ps1 -Install           # 게시 후 Windows 서비스 설치
#   .\publish.ps1 -Uninstall         # Windows 서비스 제거
#
# 관리자 권한 필요: -Install / -Uninstall 사용 시

param(
    [switch]$Install,
    [switch]$Uninstall,
    [string]$ServiceName = "BodaVmsWeb",
    [string]$PublishDir = ".\publish"
)

$ErrorActionPreference = "Stop"

$projectPath = "BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj"
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

Write-Host ""
Write-Host "Done." -ForegroundColor Green
