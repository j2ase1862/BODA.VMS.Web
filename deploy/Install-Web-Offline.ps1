<#
.SYNOPSIS
  BODA.VMS.Web 오프라인(에어갭) 현장 PC 설치 스크립트 — 빌드 없이 복사 + 서비스 등록만.

.DESCRIPTION
  인터넷/.NET SDK 가 없는 현장 PC 용. 개발 PC 에서 미리 만든 self-contained
  publish 산출물을 USB 등으로 가져와 이 스크립트로 설치한다.

  [개발 PC — 인터넷 OK]
    dotnet publish .\BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -o .\publish
    → publish\ 폴더와 이 스크립트를 USB 에 복사

  [현장 PC — 관리자 PowerShell]
    .\Install-Web-Offline.ps1 -SourcePath E:\publish

  수행 내용:
    1. publish 산출물 → 설치 폴더 복사 (appsettings.Production.json 은 보존)
    2. 데이터 폴더(C:\ProgramData\BODA\VMS) 생성 — SQLite DB 는 첫 실행 시 자동 생성
    3. appsettings.Production.json 이 없으면 생성
       (Jwt:Key 무작위 자동 생성 + Initial:AdminPassword — 미지정 시 무작위 생성 후 화면 출력)
    4. Windows Service 등록 (부팅 자동시작 + 실패 시 자동 재시작)
    5. 방화벽 인바운드 규칙 추가 (기본 5292/TCP)
    6. 서비스 시작 + 로컬 스모크 테스트

  재실행(업데이트)에도 안전: 기존 appsettings.Production.json / DB 는 절대
  덮어쓰지 않고, 서비스는 중지 → 파일 동기화 → 재등록 → 시작 순으로 처리.

  ⚠ HTTPS 미지원 (의도): 오프라인 환경은 Cloudflare TLS 종단이 없고 인증서
  수동 배포가 필요하므로 폐쇄 LAN 전제의 HTTP 로 구성한다. HTTPS 가 요구되면
  .pfx 를 별도 준비해 Kestrel 설정에 지정할 것.

.PARAMETER SourcePath
  개발 PC 에서 만든 publish 산출물 폴더 (BODA.VMS.Web.exe 포함). 기본값은
  스크립트 위치 기준 ..\publish (리포째 USB 에 복사한 경우 그대로 동작).

.PARAMETER InstallPath
  설치 폴더. 기본값 C:\Deploy\BodaVmsWeb (런북 §7 권장 위치).

.PARAMETER Port
  Kestrel HTTP 포트. 기본값 5292 (운영 런북과 동일).

.PARAMETER ServiceName
  Windows Service 이름. 기본값 BodaVmsWeb (운영 런북과 동일).

.PARAMETER DataDir
  SQLite DB 폴더. 기본값 C:\ProgramData\BODA\VMS (appsettings.json 기본
  ConnectionString 경로와 일치).

.PARAMETER JwtKey
  appsettings.Production.json 신규 생성 시 사용할 JWT 서명 키 (32자 이상).
  생략하면 암호학적 무작위 키를 자동 생성. 기존 설정 파일이 있으면 무시됨.

.PARAMETER AdminPassword
  첫 부팅 시 시드할 초기 admin 계정 비밀번호 (8자 이상, 12자 이상 권장).
  앱은 admin 계정이 DB 에 없으면 Initial:AdminPassword 없이는 부팅을 거부한다
  (GS 보안 baseline — 디폴트 admin/admin 금지). 생략하면 무작위 16자를 생성해
  설치 완료 화면에 출력한다. 기존 설정 파일이 있으면 무시됨.

.EXAMPLE
  # USB(E:)의 publish 산출물로 신규 설치
  .\Install-Web-Offline.ps1 -SourcePath E:\publish

.EXAMPLE
  # 업데이트 배포 (설정/DB 보존, 파일만 교체)
  .\Install-Web-Offline.ps1 -SourcePath E:\publish

.NOTES
  요구사항: 관리자 권한, Windows x64. .NET 런타임 불필요(self-contained).
  제거: .\Uninstall-Web.ps1 -ServiceName BodaVmsWeb
#>

[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$InstallPath = "C:\Deploy\BodaVmsWeb",
    [int]$Port = 5292,
    [string]$ServiceName = "BodaVmsWeb",
    [string]$ServiceDisplayName = "BODA VMS Web Server",
    [string]$ServiceDescription = "BODA Vision Management System - Web Server",
    [string]$DataDir = "C:\ProgramData\BODA\VMS",
    [string]$JwtKey,
    [string]$AdminPassword
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExeName = "BODA.VMS.Web.exe"

if (-not $SourcePath) {
    $SourcePath = Join-Path (Split-Path -Parent $ScriptDir) "publish"
}

# ----- 관리자 권한 확인 -----
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한으로 PowerShell 을 실행해야 합니다. (서비스 등록 + 방화벽)"
    exit 1
}

# ----- 산출물 검증 -----
$srcExe = Join-Path $SourcePath $ExeName
if (-not (Test-Path $srcExe)) {
    Write-Error @"
publish 산출물을 찾지 못했습니다: $srcExe
개발 PC 에서 먼저 빌드하세요:
  dotnet publish .\BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\publish
그 후: .\Install-Web-Offline.ps1 -SourcePath <publish 폴더>
"@
    exit 1
}

$resolvedSource  = [System.IO.Path]::GetFullPath($SourcePath)
$resolvedInstall = [System.IO.Path]::GetFullPath($InstallPath)
if ($resolvedSource -eq $resolvedInstall) {
    Write-Error "SourcePath 와 InstallPath 가 같습니다. 산출물 폴더를 설치 폴더로 직접 지정하지 마세요."
    exit 1
}

Write-Host ""
Write-Host "===== BODA.VMS.Web 오프라인 설치 =====" -ForegroundColor Cyan
Write-Host "  산출물     : $resolvedSource"
Write-Host "  설치 폴더  : $resolvedInstall"
Write-Host "  포트       : $Port (HTTP)"
Write-Host "  서비스 이름: $ServiceName"
Write-Host "  DB 폴더    : $DataDir"
Write-Host ""

# ----- 기존 서비스 중지 (업데이트 케이스) -----
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[1/6] 기존 서비스 발견 — 중지 (업데이트 모드)" -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }
} else {
    Write-Host "[1/6] 신규 설치" -ForegroundColor Green
}

# ----- 파일 복사 (운영 설정 보존) -----
Write-Host "[2/6] 파일 복사..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $resolvedInstall | Out-Null
robocopy $resolvedSource $resolvedInstall /MIR /XF "appsettings.Production.json" /NJH /NJS /NDL /NFL /NP | Out-Null
# robocopy 종료코드: 0~7 정상 / 8+ 실패
if ($LASTEXITCODE -ge 8) {
    Write-Error "robocopy 실패 (exit=$LASTEXITCODE)"
    exit 1
}

# ----- 데이터 폴더 -----
Write-Host "[3/6] 데이터 폴더 준비..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
# DB 파일 자체는 첫 실행 시 마이그레이션이 자동 생성 (idempotent)

# ----- appsettings.Production.json -----
$prodConfig = Join-Path $resolvedInstall "appsettings.Production.json"
$dbPath = Join-Path $DataDir "BodaVision.db"
$generatedAdminPassword = $null
if (Test-Path $prodConfig) {
    Write-Host "[4/6] appsettings.Production.json 존재 — 보존 (덮어쓰지 않음)" -ForegroundColor Gray
    $existingConfig = Get-Content $prodConfig -Raw | ConvertFrom-Json
    if ($null -eq $existingConfig.Jwt -or $null -eq $existingConfig.Jwt.Key -or $existingConfig.Jwt.Key.Length -lt 32) {
        Write-Warning "기존 설정의 Jwt:Key 가 없거나 32자 미만 — 앱이 부팅을 거부합니다. 파일을 수정하세요: $prodConfig"
    }
    # DB 가 아직 없으면 첫 부팅 시 admin 시드가 필요 → Initial:AdminPassword 필수
    if (-not (Test-Path $dbPath)) {
        $existingInitial = $existingConfig.Initial
        if ($null -eq $existingInitial -or [string]::IsNullOrWhiteSpace($existingInitial.AdminPassword)) {
            Write-Warning "DB 가 아직 없는데 기존 설정에 Initial:AdminPassword 가 없음 — 첫 부팅이 거부됩니다."
            Write-Warning "파일에 추가하세요: `"Initial`": { `"AdminPassword`": `"<8자 이상>`" } → $prodConfig"
        }
    }
} else {
    Write-Host "[4/6] appsettings.Production.json 생성..." -ForegroundColor Green
    if (-not $JwtKey) {
        # 암호학적 무작위 64바이트 → Base64 88자 (요구: 32자 이상)
        $bytes = New-Object byte[] 64
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $JwtKey = [Convert]::ToBase64String($bytes)
    }
    if ($JwtKey.Length -lt 32) {
        Write-Error "-JwtKey 는 32자 이상이어야 합니다 (앱 부팅 검증)."
        exit 1
    }
    if (-not $AdminPassword) {
        # 무작위 16자 (영문 대소문자 + 숫자) — 설치 완료 화면에 출력
        $charset = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789"
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $pwBytes = New-Object byte[] 16
        $rng.GetBytes($pwBytes)
        $AdminPassword = -join ($pwBytes | ForEach-Object { $charset[$_ % $charset.Length] })
        $generatedAdminPassword = $AdminPassword
    }
    if ($AdminPassword.Length -lt 8) {
        Write-Error "-AdminPassword 는 8자 이상이어야 합니다 (앱 부팅 검증, 12자 이상 권장)."
        exit 1
    }
    $config = [ordered]@{
        Jwt = [ordered]@{ Key = $JwtKey }
        # 첫 부팅 시 admin 계정 시드용 — 계정 생성 후에는 무시됨 (원하면 삭제 가능)
        Initial = [ordered]@{ AdminPassword = $AdminPassword }
        ConnectionStrings = [ordered]@{ DefaultConnection = "Data Source=$dbPath" }
    }
    $json = $config | ConvertTo-Json -Depth 5
    # BOM 없는 UTF-8 (PS 5.1 Out-File 기본은 UTF-16 이라 사용 금지)
    [System.IO.File]::WriteAllText($prodConfig, $json, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "    Jwt:Key 자동 생성됨 — 이 파일이 유일한 사본입니다. 백업 권장: $prodConfig" -ForegroundColor Yellow
}

# ----- 서비스 등록 (항상 재등록 — binPath 를 현재 설치 경로와 일치 보장) -----
Write-Host "[5/6] Windows Service 등록..." -ForegroundColor Green
$exePath = Join-Path $resolvedInstall $ExeName
if ($existing) {
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}
& sc.exe create $ServiceName `
    binPath= "`"$exePath`" --environment Production --urls=http://0.0.0.0:$Port" `
    start= auto `
    DisplayName= $ServiceDisplayName | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "서비스 등록 실패 (exit=$LASTEXITCODE)"
    exit 1
}
& sc.exe description $ServiceName $ServiceDescription | Out-Null
# 실패 시 자동 재시작: 10초 → 30초 → 60초, 카운터는 24시간 후 리셋
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null

# ----- 방화벽 (LAN 접속용) -----
$ruleName = "BODA VMS Web ($Port/TCP)"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $ruleName `
    -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $Port -Profile Any | Out-Null

# ----- 시작 + 스모크 테스트 -----
Write-Host "[6/6] 서비스 시작..." -ForegroundColor Green
Start-Service -Name $ServiceName
Start-Sleep -Seconds 8

$svc = Get-Service -Name $ServiceName
$smokeOk = $false
if ($svc.Status -eq 'Running') {
    try {
        $resp = Invoke-WebRequest "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 15
        $smokeOk = ($resp.StatusCode -eq 200)
    } catch { }
}

Write-Host ""
if ($smokeOk) {
    $lanIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" } |
        Select-Object -First 1 -ExpandProperty IPAddress)
    Write-Host "===== 설치 완료 =====" -ForegroundColor Cyan
    Write-Host "  로컬 접속 : http://localhost:$Port"
    if ($lanIp) {
        Write-Host "  LAN 접속  : http://${lanIp}:$Port  (다른 PC / VMS 클라이언트 / 스마트글라스)"
    }
    Write-Host "  서비스    : $ServiceName ($($svc.Status), 부팅 자동시작)"
    Write-Host "  운영 설정 : $prodConfig"
    Write-Host "  DB 경로   : $DataDir\BodaVision.db (첫 실행 시 자동 생성)"
    if ($generatedAdminPassword) {
        Write-Host ""
        Write-Host "  ⚠ 초기 admin 비밀번호 (자동 생성 — 지금 기록해 두세요):" -ForegroundColor Yellow
        Write-Host "     아이디: admin / 비밀번호: $generatedAdminPassword" -ForegroundColor Yellow
        Write-Host "     첫 로그인 후 비밀번호를 변경하세요." -ForegroundColor Yellow
    }
} else {
    Write-Warning "서비스 상태: $($svc.Status) — 스모크 테스트 실패 (http://localhost:$Port/)"
    Write-Warning "이벤트 뷰어 → Windows 로그 → Application 에서 '.NET Runtime' 소스 확인."
    Write-Warning "흔한 원인: appsettings.Production.json 의 Jwt:Key 누락/32자 미만, DB 폴더 권한."
    exit 1
}
