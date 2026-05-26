<#
.SYNOPSIS
  BODA.VMS.Web 을 Kestrel + Windows Service 로 설치.

.DESCRIPTION
  1. dotnet publish self-contained → 지정 폴더로 출력
  2. Windows Service 등록 (sc.exe) — 부팅 시 자동 시작
  3. 방화벽 인바운드 규칙 추가 (port 7144)
  4. 서비스 시작

  IIS 없이 단독 Kestrel 로 동작. SQLite DB 는 LocalAppData 에 자동 생성.

.PARAMETER InstallPath
  설치 폴더. 기본값 C:\Program Files\BODA VMS Web\

.PARAMETER Port
  Listen 포트. 기본값 7144 (HTTPS).

.PARAMETER ServiceName
  Windows Service 이름. 기본값 "BODA-VMS-Web".

.EXAMPLE
  # 관리자 PowerShell 에서 실행
  .\Install-Web.ps1

.EXAMPLE
  .\Install-Web.ps1 -Port 8443 -InstallPath "D:\BODA VMS Web"

.NOTES
  요구사항:
    - 관리자 권한 (Windows Service 등록용)
    - .NET 8 SDK (빌드 측), 별도 .NET 런타임은 self-contained 라 불필요
    - PowerShell 5.1+ 또는 PowerShell 7
#>

[CmdletBinding()]
param(
    [string]$InstallPath = "C:\Program Files\BODA VMS Web",
    [int]$Port = 7144,
    [string]$ServiceName = "BODA-VMS-Web",
    [string]$ServiceDisplayName = "BODA VMS Web Service",
    [string]$ServiceDescription = "BODA Vision Management System web server (Kestrel, port $Port)."
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$WebProject = Join-Path $RepoRoot "BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj"

# ----- 관리자 권한 확인 -----
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한으로 PowerShell 을 실행해야 합니다."
    exit 1
}

Write-Host ""
Write-Host "===== BODA.VMS.Web 설치 시작 =====" -ForegroundColor Cyan
Write-Host "  설치 폴더 : $InstallPath"
Write-Host "  포트       : $Port"
Write-Host "  서비스 이름: $ServiceName"
Write-Host ""

# ----- 기존 서비스 중지 (있다면) -----
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "기존 서비스 발견 — 중지 후 재설치합니다." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
}

# ----- Publish -----
Write-Host "[1/4] dotnet publish self-contained..." -ForegroundColor Green
if (-not (Test-Path $WebProject)) {
    Write-Error "BODA.VMS.Web.csproj 를 찾지 못했습니다: $WebProject"
    exit 1
}

if (Test-Path $InstallPath) {
    # 기존 폴더 정리 — DB / 로그는 LocalAppData 에 있어 영향 없음
    Write-Host "  기존 설치 폴더 삭제 중..."
    Remove-Item -Recurse -Force $InstallPath
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

& dotnet publish $WebProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o "$InstallPath"

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish 실패 (exit $LASTEXITCODE)"
    exit 1
}

# ----- 서비스 등록 -----
Write-Host "[2/4] Windows Service 등록..." -ForegroundColor Green
$ExePath = Join-Path $InstallPath "BODA.VMS.Web.exe"
if (-not (Test-Path $ExePath)) {
    Write-Error "Publish 출력에 BODA.VMS.Web.exe 가 없습니다: $ExePath"
    exit 1
}

if ($existing) {
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

& sc.exe create $ServiceName `
    binPath= "`"$ExePath`" --urls=https://0.0.0.0:$Port" `
    start= auto `
    DisplayName= $ServiceDisplayName | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "Service 등록 실패 (exit $LASTEXITCODE)"
    exit 1
}

& sc.exe description $ServiceName $ServiceDescription | Out-Null

# 실패 시 자동 재시작 (5초 후, 최대 3회)
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# ----- 방화벽 -----
Write-Host "[3/4] 방화벽 인바운드 규칙 추가..." -ForegroundColor Green
$ruleName = "BODA VMS Web ($Port/TCP)"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $ruleName `
    -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $Port `
    -Profile Any | Out-Null

# ----- 서비스 시작 -----
Write-Host "[4/4] 서비스 시작..." -ForegroundColor Green
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $ServiceName
if ($svc.Status -ne 'Running') {
    Write-Warning "서비스가 시작되지 않았습니다. Event Viewer → Application 로그 확인 권장."
    Write-Warning "수동 시작: Start-Service $ServiceName"
} else {
    Write-Host ""
    Write-Host "===== 설치 완료 =====" -ForegroundColor Cyan
    Write-Host "  접속 URL : https://localhost:$Port"
    Write-Host "  서비스 상태: $($svc.Status)"
    Write-Host "  제거 명령 : .\Uninstall-Web.ps1"
}
