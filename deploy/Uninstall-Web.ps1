<#
.SYNOPSIS
  BODA.VMS.Web Windows Service 와 설치 폴더, 방화벽 규칙 제거.

.NOTES
  SQLite DB (LocalAppData) 는 보존됩니다 — 데이터 손실 방지.
  완전 제거하려면 Remove-Item "$env:LOCALAPPDATA\BODA\VMS" -Recurse 추가 실행.
#>

[CmdletBinding()]
param(
    [string]$InstallPath = "C:\Program Files\BODA VMS Web",
    [int]$Port = 7144,
    [string]$ServiceName = "BODA-VMS-Web"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한으로 실행해야 합니다."
    exit 1
}

Write-Host "===== BODA.VMS.Web 제거 =====" -ForegroundColor Cyan

# 서비스 중지/제거
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "[1/3] 서비스 중지/제거..."
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    & sc.exe delete $ServiceName | Out-Null
} else {
    Write-Host "서비스 없음 — 스킵"
}

# 방화벽 규칙 제거
$ruleName = "BODA VMS Web ($Port/TCP)"
$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($rule) {
    Write-Host "[2/3] 방화벽 규칙 제거..."
    Remove-NetFirewallRule -DisplayName $ruleName
} else {
    Write-Host "방화벽 규칙 없음 — 스킵"
}

# 설치 폴더 제거
if (Test-Path $InstallPath) {
    Write-Host "[3/3] 설치 폴더 제거: $InstallPath"
    Remove-Item -Recurse -Force $InstallPath
} else {
    Write-Host "설치 폴더 없음 — 스킵"
}

Write-Host ""
Write-Host "===== 제거 완료 =====" -ForegroundColor Cyan
Write-Host "DB 보존 위치: $env:LOCALAPPDATA\BODA\VMS"
Write-Host "DB 까지 완전 제거하려면: Remove-Item `"$env:LOCALAPPDATA\BODA\VMS`" -Recurse"
