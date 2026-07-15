<#
.SYNOPSIS
  현장 admin 로그인 401 진단 — 설정 파일 + DB 감사 로그로 실패 사유 특정.

.DESCRIPTION
  로그인 API 는 계정 열거 방지를 위해 모든 실패를 동일한 401 로 응답한다.
  실제 사유(unknown username / locked out / wrong password / not approved)는
  AuditLogs 테이블에만 기록되므로, 이 스크립트가 DB 를 읽기 전용으로 열어
  최근 인증 이벤트와 계정 상태를 보여준다. 서비스 실행 중에도 안전(WAL 읽기).

  자주 있는 원인: 설치 시 -AdminPassword 미지정(GUI 런처 포함)이면 무작위
  16자가 자동 생성되어 appsettings.Production.json 의 Initial.AdminPassword 에
  저장된다 — 즉 짐작하는 비밀번호가 아니라 그 파일 안의 값이 실제 비밀번호다.

  요구사항: 이 스크립트와 같은 폴더에 sqlite3.exe (USB 패키지에 동봉).

.EXAMPLE
  .\Diagnose-AdminLogin.ps1
  .\Diagnose-AdminLogin.ps1 -TestLogin     # 대화식 로그인 테스트 포함
#>

[CmdletBinding()]
param(
    [string]$InstallPath = "C:\Deploy\BodaVmsWeb",
    [string]$DbPath = "C:\ProgramData\BODA\VMS\BodaVision.db",
    [int]$Port = 5292,
    [switch]$TestLogin
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlite = Join-Path $ScriptDir "sqlite3.exe"

Write-Host ""
Write-Host "===== admin 로그인 401 진단 =====" -ForegroundColor Cyan

# ----- [1] appsettings.Production.json — 실제 시드 비밀번호 확인 -----
Write-Host ""
Write-Host "[1] 운영 설정 파일" -ForegroundColor Green
$prodConfig = Join-Path $InstallPath "appsettings.Production.json"
if (Test-Path $prodConfig) {
    $cfg = Get-Content $prodConfig -Raw | ConvertFrom-Json
    $initUser = "admin"
    if ($cfg.Initial -and $cfg.Initial.AdminUsername) { $initUser = $cfg.Initial.AdminUsername }
    Write-Host "  파일: $prodConfig"
    if ($cfg.Initial -and $cfg.Initial.AdminPassword) {
        Write-Host "  Initial.AdminUsername : $initUser"
        Write-Host "  Initial.AdminPassword : $($cfg.Initial.AdminPassword)" -ForegroundColor Yellow
        Write-Host "  → 설치 이후 아무도 이 파일을 수정하지 않았다면 위 값이 실제 admin 비밀번호입니다." -ForegroundColor Yellow
        Write-Host "    (첫 부팅 시드에만 사용되므로, 시드 후 파일을 고쳤다면 DB 의 비밀번호는 다릅니다)"
    } else {
        Write-Host "  Initial.AdminPassword 항목 없음 — 환경변수(setx Initial__AdminPassword)로 시드했거나 삭제됨."
    }
} else {
    Write-Warning "설정 파일 없음: $prodConfig (설치 경로가 다르면 -InstallPath 지정)"
}

# ----- [2] DB — 계정 상태 + 최근 인증 감사 로그 -----
Write-Host ""
Write-Host "[2] DB 계정 상태 / 최근 인증 이벤트 (시간은 UTC — KST = UTC+9)" -ForegroundColor Green
if (-not (Test-Path $sqlite)) {
    Write-Warning "sqlite3.exe 가 없습니다: $sqlite (USB 패키지 deploy 폴더째 복사했는지 확인)"
} elseif (-not (Test-Path $DbPath)) {
    Write-Warning "DB 파일 없음: $DbPath — 서비스가 한 번도 부팅에 성공하지 못한 경우입니다."
    Write-Warning "이벤트 뷰어 → Application → '.NET Runtime' 에서 부팅 실패 사유를 확인하세요."
} else {
    Write-Host "  --- Users ---"
    & $sqlite -readonly -header -column $DbPath `
        "SELECT Id, Username, Role, IsApproved, FailedLoginCount, LockoutUntil FROM Users;"
    Write-Host ""
    Write-Host "  --- 최근 인증 이벤트 20건 (최신순) ---"
    & $sqlite -readonly -header -column $DbPath `
        "SELECT Timestamp, UserName, Action, Changes, IpAddress FROM AuditLogs WHERE EntityName IN ('Auth','KioskAuth') ORDER BY Id DESC LIMIT 20;"
    Write-Host ""
    Write-Host "  해석:" -ForegroundColor Cyan
    Write-Host "   - 'unknown username'      → admin 계정 자체가 없음 (다른 이름으로 시드됐는지 Users 확인)"
    Write-Host "   - 'wrong password (n/5)'  → 비밀번호 불일치. [1]의 JSON 값으로 다시 시도"
    Write-Host "   - 'locked out until ...'  → 5회 연속 실패 잠금. 해당 UTC 시각까지 대기 (15분) 후 정확한 비밀번호로 1회만 시도"
    Write-Host "   - 'not approved'          → 비밀번호는 맞음. 계정 승인 필요 (관리자 계정으로 승인)"
}

# ----- [3] (선택) 로그인 테스트 -----
if ($TestLogin) {
    Write-Host ""
    Write-Host "[3] 로그인 테스트 — 실패는 잠금 카운트를 올립니다. 확실한 값으로 1회만." -ForegroundColor Green
    $u = Read-Host "  아이디 (기본 admin)"
    if ([string]::IsNullOrWhiteSpace($u)) { $u = "admin" }
    $p = Read-Host "  비밀번호"
    try {
        $body = @{ username = $u; password = $p } | ConvertTo-Json
        $r = Invoke-RestMethod "http://localhost:$Port/api/auth/login" -Method Post -ContentType "application/json" -Body $body
        Write-Host "  ✓ 로그인 성공 — role: $($r.role), 토큰 발급됨" -ForegroundColor Green
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 429) {
            Write-Warning "429 — IP 레이트리밋 (분당 5회 초과). 1분 후 재시도."
        } elseif ($status -eq 401) {
            Write-Warning "401 — 위 [2]의 감사 로그를 다시 실행해 방금 실패의 사유를 확인하세요."
        } else {
            Write-Warning "요청 실패: $($_.Exception.Message) (서비스 기동 여부: Get-Service BodaVmsWeb)"
        }
    }
}

# ----- 복구 절차 안내 -----
Write-Host ""
Write-Host "===== 비밀번호를 알 수 없을 때 복구 (DB 보존) =====" -ForegroundColor Cyan
Write-Host @"
  권장: 같은 폴더의 Reset-AdminPassword.cmd 를 더블클릭 → 새 비밀번호만 입력하면
        설정 기록 → 서비스 재시작 → 로그인 확인까지 자동 (명령어 입력 불필요).
        (Initial:ForceAdminPasswordReset 지원 게시본 필요 — 2026-07-15 PR #44 이후 빌드)

  수동 (구버전 서버 — 별도 계정으로 우회):
  1. Stop-Service BodaVmsWeb
  2. $prodConfig 의 Initial 섹션을 다음으로 교체:
       "Initial": { "AdminUsername": "admin2", "AdminPassword": "<12자 이상 새 비밀번호>" }
  3. Start-Service BodaVmsWeb   → admin2 가 Admin 권한으로 신규 시드됨 (기존 데이터 무손실)
  4. admin2 로 로그인 → 사용자 관리에서 기존 admin 정리/비밀번호 재설정
"@
