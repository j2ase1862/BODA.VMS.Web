<#
.SYNOPSIS
  admin 비밀번호 현장 리셋 — 명령어 입력 없이 대화식으로 진행 (DB 보존).

.DESCRIPTION
  비밀번호를 분실했거나 "설정 파일의 값으로 로그인이 안 되는" 상황
  (Initial:AdminPassword 는 첫 시드에만 쓰이므로 계정 생성 후 파일을 고쳐도
  반영되지 않음 — 401 의 최다 원인)을 위한 공식 복구 도구.

  Reset-AdminPassword.cmd 더블클릭 → 관리자 권한 자동 요청 → 새 비밀번호만
  입력하면 나머지는 자동:
    1. appsettings.Production.json 백업 후 Initial.AdminPassword +
       Initial.ForceAdminPasswordReset=true 기록
    2. 서비스 재시작 → 부팅 시 서버가 비밀번호 강제 재설정 + 잠금 해제
       (감사 로그 AdminPasswordReset 기록 — AdminAccountBootstrap.cs)
    3. 새 비밀번호로 로그인 자동 확인
    4. 성공 시 ForceAdminPasswordReset 플래그 자동 제거 (켜둔 채 두면
       매 부팅마다 재적용되어 이후 UI 에서 바꾼 비밀번호가 되돌아감)

  요구사항: v1.2+ 서버 (Initial:ForceAdminPasswordReset 지원). DB 는 건드리지
  않으므로 운영 데이터 무손실.

.EXAMPLE
  .\Reset-AdminPassword.ps1                       # 대화식 (권장)
  .\Reset-AdminPassword.ps1 -InstallPath D:\Web   # 설치 경로가 다른 경우
#>

[CmdletBinding()]
param(
    [string]$InstallPath = "C:\Deploy\BodaVmsWeb",
    [string]$ServiceName = "BodaVmsWeb",
    [int]$Port = 5292
)

$ErrorActionPreference = "Stop"

# ----- 관리자 권한 자동 승격 (더블클릭 지원) -----
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "관리자 권한이 필요합니다 — UAC 창에서 [예]를 눌러주세요..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit",
        "-File", "`"$($MyInvocation.MyCommand.Path)`"",
        "-InstallPath", "`"$InstallPath`"", "-ServiceName", $ServiceName, "-Port", $Port
    )
    exit 0
}

Write-Host ""
Write-Host "===== admin 비밀번호 리셋 =====" -ForegroundColor Cyan
Write-Host "  (운영 DB 는 보존됩니다 — 비밀번호와 로그인 잠금만 초기화)"
Write-Host ""

# ----- [1] 설정 파일 확인 -----
$prodConfig = Join-Path $InstallPath "appsettings.Production.json"
if (-not (Test-Path $prodConfig)) {
    Write-Warning "설정 파일이 없습니다: $prodConfig"
    Write-Warning "설치 경로가 다르면 -InstallPath 로 지정하세요. (예: 구버전 system32\publish)"
    exit 1
}
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Warning "서비스 '$ServiceName' 이 없습니다. 설치 여부를 확인하세요 (Install-Web-Offline.ps1)."
    exit 1
}
$cfg = Get-Content $prodConfig -Raw | ConvertFrom-Json
$username = "admin"
if ($cfg.Initial -and $cfg.Initial.AdminUsername) { $username = $cfg.Initial.AdminUsername }
Write-Host "[1/5] 대상 계정: $username  (설정: $prodConfig)" -ForegroundColor Green

# ----- [2] 새 비밀번호 입력 (화면에 표시 안 됨) -----
Write-Host ""
while ($true) {
    $sec1 = Read-Host "  새 비밀번호 입력 (8자 이상, 12자 이상 권장)" -AsSecureString
    $sec2 = Read-Host "  새 비밀번호 다시 입력 (확인)" -AsSecureString
    $p1 = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec1))
    $p2 = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec2))
    if ($p1 -ne $p2) { Write-Warning "두 입력이 다릅니다. 다시 입력하세요."; continue }
    if ($p1.Length -lt 8) { Write-Warning "8자 미만입니다. 다시 입력하세요."; continue }
    break
}
$newPassword = $p1

# ----- [3] 설정 기록 (백업 후) -----
Write-Host ""
Write-Host "[2/5] 설정 파일 백업 + 리셋 플래그 기록..." -ForegroundColor Green
$backup = "$prodConfig.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Copy-Item $prodConfig $backup -Force
if (-not $cfg.Initial) {
    $cfg | Add-Member -NotePropertyName "Initial" -NotePropertyValue ([pscustomobject]@{}) -Force
}
$cfg.Initial | Add-Member -NotePropertyName "AdminPassword" -NotePropertyValue $newPassword -Force
$cfg.Initial | Add-Member -NotePropertyName "ForceAdminPasswordReset" -NotePropertyValue $true -Force
# BOM 없는 UTF-8 (PS 5.1 Out-File 기본은 UTF-16 이라 사용 금지)
[System.IO.File]::WriteAllText($prodConfig, ($cfg | ConvertTo-Json -Depth 10),
    (New-Object System.Text.UTF8Encoding $false))

# ----- [4] 서비스 재시작 → 부팅 시드가 리셋 적용 -----
Write-Host "[3/5] 서비스 재시작 (리셋 적용)..." -ForegroundColor Green
Restart-Service -Name $ServiceName -Force
$healthy = $false
foreach ($i in 1..15) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
}
if (-not $healthy) {
    Write-Warning "서비스가 30초 내에 준비되지 않았습니다. 이벤트 뷰어 → Application 확인."
    Write-Warning "설정은 백업에서 복원 가능: $backup"
    exit 1
}

# ----- [5] 새 비밀번호 로그인 확인 -----
Write-Host "[4/5] 새 비밀번호 로그인 확인..." -ForegroundColor Green
$loginOk = $false
try {
    $body = @{ username = $username; password = $newPassword } | ConvertTo-Json
    $r = Invoke-RestMethod "http://localhost:$Port/api/auth/login" `
        -Method Post -ContentType "application/json" -Body $body
    $loginOk = $true
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($status -eq 429) {
        Write-Warning "429 레이트리밋 — 리셋 자체는 적용됐을 수 있습니다. 1분 후 브라우저에서 직접 확인하세요."
    } else {
        Write-Warning "로그인 확인 실패 (status=$status) — Diagnose-AdminLogin.ps1 로 사유를 확인하세요."
    }
}

# ----- [6] 성공 시 플래그 제거 (매 부팅 재적용 방지) -----
if ($loginOk) {
    Write-Host "[5/5] 리셋 플래그 제거 (일회성 보장)..." -ForegroundColor Green
    $cfg.Initial.PSObject.Properties.Remove("ForceAdminPasswordReset")
    [System.IO.File]::WriteAllText($prodConfig, ($cfg | ConvertTo-Json -Depth 10),
        (New-Object System.Text.UTF8Encoding $false))
    Write-Host ""
    Write-Host "===== 완료 =====" -ForegroundColor Cyan
    Write-Host "  ✓ '$username' 비밀번호가 변경되고 로그인 잠금이 해제되었습니다." -ForegroundColor Green
    Write-Host "  ✓ 새 비밀번호로 로그인 확인됨: http://localhost:$Port"
    Write-Host "  - 감사 로그에 AdminPasswordReset 이벤트가 기록되었습니다."
    Write-Host "  - 설정 백업: $backup (평문 비밀번호 포함 — 보관에 주의)"
} else {
    Write-Host ""
    Write-Warning "로그인 확인에 실패해 ForceAdminPasswordReset 플래그를 남겨뒀습니다."
    Write-Warning "다음 서비스 재시작 때 다시 리셋을 시도합니다. 원인 파악: .\Diagnose-AdminLogin.ps1"
    exit 1
}
