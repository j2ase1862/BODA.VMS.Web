# BODA.VMS.Web — 운영 배포 런북 (boda-vms.com)

> **대상**: `boda-vms.com` 운영 환경 (Cloudflare Tunnel 기반)
> **마지막 검증**: 2026-05-26 — 예측 인프라 v1 배포 시
> **읽는 대상**: 차기 배포 담당자 또는 다음 세션의 Claude Code
> **소요 시간**: 처음 ~ 10분 (빌드 3분 + 백업·배포 3분 + 검증)

이 문서는 **이미 운영 중인 `boda-vms.com`** 환경에 코드 변경을 무중단(에 가까운)으로 반영하는 절차입니다. 처음 셋업 절차가 아닙니다. 처음 셋업은 `DEPLOYMENT_GUIDE.md` 참조.

---

## 1. 환경 개요

```
사용자 (브라우저)
   │ HTTPS
   ▼
┌─────────────────┐
│ Cloudflare Edge │ ← boda-vms.com (인증서 + WAF)
└────────┬────────┘
         │ 암호화된 Tunnel
         ▼
┌─────────────────────────────────────────┐
│ 서버 PC (Windows)                       │
│  • cloudflared 서비스 (RUNNING)         │
│  • BodaVmsWeb 서비스 (RUNNING)          │
│    └─ Kestrel http://localhost:5292     │
│  • BodaVision.db (C:\ProgramData\...)   │
└─────────────────────────────────────────┘
```

### 1.1 고정 사실 (변경되면 이 문서 갱신 필수)

| 항목 | 값 |
|---|---|
| 운영 게시 폴더 | `C:\WINDOWS\system32\publish\` |
| 운영 DB 경로 | `C:\ProgramData\BODA\VMS\BodaVision.db` |
| 운영 서비스 이름 | `BodaVmsWeb` |
| Kestrel 포트 | `5292` (HTTP, localhost only — Cloudflare가 TLS 종단) |
| Tunnel 이름 | `boda-vms` |
| Tunnel ID | `b02127ff-0798-4c4b-80d1-27dfce30c7e3` |
| 백업 폴더 패턴 | `C:\Backup\BodaVms-{yyyyMMdd-HHmmss}\` |

### 1.2 왜 이 구성인가
- Cloudflare Tunnel → **공인 IP/포트포워딩/방화벽 오픈 불필요**
- HTTPS 인증서는 Cloudflare 자동 처리 → **갱신 신경 안 써도 됨**
- Kestrel은 `http://0.0.0.0:5292`만 → 로컬 PC 안에서만 노출

### 1.3 ⚠ 알려진 비표준
운영 게시 폴더가 `C:\WINDOWS\system32\publish\`에 있습니다(과거 publish.ps1을 관리자 PowerShell의 기본 위치에서 실행한 잔재). **동작은 정상이지만 권장 위치는 아닙니다.** 시간 날 때 §7 절차로 `C:\Deploy\BodaVmsWeb`로 이전 권장.

---

## 2. 사전 점검 (배포 전 5분)

### 2.1 권한 확인
배포에는 **관리자 권한 PowerShell이 필수**입니다 (system32 쓰기 + Stop-Service).

```powershell
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
```
→ `True` 가 나와야 함. 창 제목에 **"관리자:"** 가 보여야 함.

`False`라면: 시작 메뉴 → "powershell" 검색 → 우클릭 → **관리자 권한으로 실행** → UAC 동의.

### 2.2 현재 서비스 상태
```powershell
Get-Service BodaVmsWeb, cloudflared | Format-Table Name, Status
```
둘 다 `Running`이어야 정상 운영 상태.

### 2.3 로컬 git 상태
```powershell
cd D:\Project\BODA.VMS.Web
git status
git log --oneline -3
```
배포할 커밋이 **master 또는 PR 머지된 브랜치**에 있어야 함. 미커밋 변경이 있다면 먼저 커밋.

### 2.4 디스크 여유
백업 약 200MB + 새 게시본 약 200MB가 필요. `C:` 드라이브에 1GB 이상 여유 확인.

---

## 3. 표준 배포 절차 (관리자 PowerShell)

### Step 1 — 빌드 (일반 권한 PS 또는 셸 OK)

```powershell
cd D:\Project\BODA.VMS.Web
Remove-Item .\publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=false `
    -o .\publish
```
- 약 2~3분 소요. 워닝은 OK, 에러는 NG.
- 산출물: `D:\Project\BODA.VMS.Web\publish\` (~150MB)

### Step 2 — 백업 (관리자 권한 권장 — system32 읽기 일관성)

```powershell
$ts = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = "C:\Backup\BodaVms-$ts"
New-Item -ItemType Directory -Force -Path "$backup\old-publish" | Out-Null

# DB 3종 (.db / .db-wal / .db-shm)
Copy-Item "C:\ProgramData\BODA\VMS\BodaVision.db*" $backup -Force

# Production 설정 (JWT Key 등 운영값 보존 — 가장 중요)
Copy-Item "C:\WINDOWS\system32\publish\appsettings.Production.json" $backup -Force

# 현재 운영 게시 폴더 전체 (롤백용)
Copy-Item "C:\WINDOWS\system32\publish\*" "$backup\old-publish\" -Recurse -Force

Write-Host "백업 위치: $backup" -ForegroundColor Cyan
$global:LastBackup = $backup    # 같은 세션 내 롤백용 변수
```

### Step 3 — 서비스 중지 → 동기화 → 시작 (관리자 PS 필수)

```powershell
$publishDir = "D:\Project\BODA.VMS.Web\publish"
$prodDir    = "C:\WINDOWS\system32\publish"

# 1) 서비스 중지
Stop-Service BodaVmsWeb
Start-Sleep 3
Get-Service BodaVmsWeb | Format-Table Name, Status

# 2) 새 게시본 → 운영 폴더 동기화 (Production 설정 제외)
robocopy $publishDir $prodDir /MIR /XF "appsettings.Production.json" /NJH /NJS /NDL /NFL /NP
# robocopy 종료코드: 0~7 정상 / 8+ 실패
if ($LASTEXITCODE -ge 8) { throw "robocopy 실패 (exit=$LASTEXITCODE)" }

# 3) Production 설정 복원 (안전망 — /XF 로 이미 제외했지만 확실히)
Copy-Item "$backup\appsettings.Production.json" "$prodDir\appsettings.Production.json" -Force

# 4) 서비스 시작
Start-Service BodaVmsWeb
Start-Sleep 8
Get-Service BodaVmsWeb | Format-Table Name, Status
```

### Step 4 — 스모크 테스트

```powershell
# 4-1) 로컬 Kestrel
try {
    $local = (Invoke-WebRequest http://localhost:5292/ -UseBasicParsing -TimeoutSec 10).StatusCode
    Write-Host "Local http://localhost:5292/  → $local" -ForegroundColor Green
} catch { Write-Host "Local FAILED: $_" -ForegroundColor Red }

# 4-2) Cloudflare Tunnel 경유
try {
    $cf = (Invoke-WebRequest https://boda-vms.com/ -UseBasicParsing -TimeoutSec 15).StatusCode
    Write-Host "Cloudflare https://boda-vms.com/  → $cf" -ForegroundColor Green
} catch { Write-Host "Cloudflare FAILED: $_" -ForegroundColor Red }

# 4-3) 이벤트 로그 (앱이 시작 직후 크래시했는지)
Get-EventLog -LogName Application -Newest 5 -EntryType Error -ErrorAction SilentlyContinue |
    Where-Object { $_.TimeGenerated -gt (Get-Date).AddMinutes(-3) } |
    Format-Table TimeGenerated, Source, Message -Wrap
```

둘 다 `200`이면 성공. 이어서 브라우저에서:
- `https://boda-vms.com/login` → admin 로그인
- 변경한 페이지 진입 (예: `/forecast`, `/maintenance`, …)
- VMS 클라이언트가 heartbeat 보내는지 (`/clients` 페이지 online 점)

### Step 5 — Cloudflare 캐시 퍼지 (Blazor 클라이언트 변경 시 권장)

**언제:** Blazor WASM 클라이언트(`BODA.VMS.Web.Client`)가 재빌드된 배포라면 권장. 클라이언트가 바뀌면 `blazor.boot.json` + 해시된 `_framework/*.dll`/`.wasm` 이 바뀌는데, robocopy `/MIR` 이 옛 해시 파일을 지우므로 — Cloudflare가 옛 `blazor.boot.json` 을 캐시하고 있으면 **재방문 사용자가 사라진 dll(404)을 요청해 앱 로딩이 깨질 수 있음.**

**왜 보통은 안 깨졌나:** 기본 Cloudflare 캐시는 `.json/.dll/.wasm` 을 캐시하지 않음. 그래서 **"Cache Everything" 페이지 규칙이 없으면** 안전. 단 그 규칙 유무와 무관하게 **배포 후 한 번 퍼지**해두면 확실함.

#### 5-A. 캐시 퍼지 — 대시보드 (가장 흔한 방법)

1. **[dash.cloudflare.com](https://dash.cloudflare.com)** 로그인
2. 계정 선택 → 도메인 목록에서 **`boda-vms.com`** 클릭
3. 왼쪽 사이드바 → **Caching** → **Configuration**
4. "Purge Cache" 영역에서 택1:
   - **간단/확실:** **Purge Everything** 버튼 → 확인 팝업에서 **Purge Everything** 다시 클릭
   - **부분만(부하 적음):** **Custom Purge** → "URL" 선택 → 아래 입력 후 Purge
     ```
     https://boda-vms.com/
     https://boda-vms.com/_framework/blazor.boot.json
     ```
5. **~30초** 전파 대기

**또는 API** (토큰 보유 시 — 자동화):
```powershell
# $env:CF_ZONE_ID, $env:CF_API_TOKEN 사전 설정 (토큰은 노출 금지)
Invoke-RestMethod -Method Post `
  -Uri "https://api.cloudflare.com/client/v4/zones/$($env:CF_ZONE_ID)/purge_cache" `
  -Headers @{ Authorization = "Bearer $($env:CF_API_TOKEN)" } `
  -ContentType "application/json" `
  -Body '{"purge_everything":true}'
```

#### 5-B. 브라우저 최종 확인 (시크릿 창 — 브라우저 로컬 캐시까지 배제)

> 시크릿/InPrivate 창을 쓰는 이유: 브라우저 로컬 캐시까지 배제해야 "진짜 새 빌드가 로딩되는지" 정확히 보임.

1. `https://boda-vms.com/login` → **admin 로그인**
2. `/andon` 진입 → 예외 없이 안돈보드 표시 (크래시 회귀 확인)
3. `/clients` → 클라이언트 **실시간 점(online)** 동작 ← SignalR(WebSocket)이 Cloudflare 통과하는지 검증
4. 변경한 페이지 정상 동작 (해당 배포 범위)

#### 5-C. (선택) "Cache Everything" 규칙 확인 — 매번 퍼지해야 하나?

Cloudflare → **Rules → Page Rules** (또는 **Caching → Cache Rules**) 에서 `boda-vms.com/*` 에 **"Cache Everything"** 이 걸려 있는지 확인:
- **있으면:** Blazor 클라이언트 바뀔 때마다 5-A 퍼지 **필수**
- **없으면(기본):** `.json/.dll/.wasm` 미캐시라 사실상 퍼지 없이도 안전

> 서버 측(서비스/Tunnel)은 손댈 것 없음. `cloudflared` 는 그대로 두면 origin 재시작 후 자동 재연결됨.

---

## 4. 롤백 (실패 시 즉시 실행)

위 절차 중 Step 4의 응답이 200이 아니거나, 운영 사용자가 에러를 보고하면:

```powershell
$backup = $global:LastBackup    # 또는 직접 "C:\Backup\BodaVms-..." 입력
Stop-Service BodaVmsWeb -ErrorAction SilentlyContinue

# 이전 게시본 통째로 되돌리기
robocopy "$backup\old-publish" "C:\WINDOWS\system32\publish" /MIR /NJH /NJS /NDL /NFL /NP

# DB도 함께 (DB 마이그레이션이 데이터 손상 일으킨 경우만 — 보통 불필요)
# Copy-Item "$backup\BodaVision.db*" "C:\ProgramData\BODA\VMS\" -Force

Start-Service BodaVmsWeb
Start-Sleep 8
Invoke-WebRequest http://localhost:5292/ -UseBasicParsing | Select-Object StatusCode
```

DB는 **마이그레이션이 idempotent**(`CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ADD COLUMN`) 하게 작성되어 있어 보통 손상되지 않습니다. 게시본만 되돌려도 거의 모든 케이스 복구됩니다.

---

## 5. 흔한 함정 (트러블슈팅)

### 5.1 `Stop-Service`: "Cannot open BodaVmsWeb service"
→ **관리자 권한 PowerShell이 아님.** §2.1로 돌아가 확인.

### 5.2 `robocopy`: "ERROR 5 Access is denied"
→ 같은 원인. system32 쓰기 권한 부족.

### 5.3 빌드는 됐는데 서비스 시작 후 즉시 멈춤
이벤트 뷰어 → Windows 로그 → 응용 프로그램에서 `.NET Runtime` 또는 `BodaVmsWeb` 소스 확인. 직전에 한 변경 중 다음을 의심:
- `Program.cs`의 raw SQL 안에 `{` 또는 `}` 문자 → EF Core `ExecuteSqlRawAsync`가 `String.Format` 으로 처리해서 FormatException. **해결**: `{{` / `}}` 로 escape.
- 신규 NuGet 패키지가 self-contained에 포함 안 됨 → `.csproj` 의 `<PackageReference>` 확인.
- `appsettings.Production.json` 가 덮어써져 잘못된 값 → §4 롤백 후 백업의 JSON 복원.

### 5.4 로컬은 200, Cloudflare는 502/523
- `cloudflared` 서비스가 죽어 있을 가능성: `Get-Service cloudflared` 확인
- Tunnel 라우팅이 `http://localhost:5292` 인지 Cloudflare Dashboard → Zero Trust → Networks → Tunnels → `boda-vms` → Routes 에서 확인 (변경된 적 없으면 그대로일 것)

### 5.5 미커밋 변경이 누적되어 있음
배포 직전에 `git status` 가 clean 이 아니면 위험. 한 번에 너무 많은 변경을 함께 푸시하면 롤백 단위가 커집니다. **작업 단위로 자주 커밋** 권장.

---

## 6. 커밋·머지·푸시 워크플로우

이 저장소는 **`feature/recipe-parameter-management` 브랜치를 재사용**하는 패턴입니다 (PR #3, #4 모두 같은 브랜치).

표준 작업 흐름:
```powershell
# 작업 시작
git checkout feature/recipe-parameter-management
git pull origin feature/recipe-parameter-management
git merge master   # master 변경 따라가기

# ... 작업 + 커밋 ...

# master 반영
git push origin feature/recipe-parameter-management   # 백업
git checkout master
git pull origin master --ff-only
git merge --no-ff feature/recipe-parameter-management -m "Merge: <요약>"
git push origin master
```

운영 배포는 항상 **master 기준**으로 수행 권장.

---

## 7. (옵션) system32 → 정돈된 위치로 이전

`C:\WINDOWS\system32\publish\` 는 비표준. 다음 정기 배포 때 한 번에 이전 가능.

```powershell
# === 관리자 PS ===
$newDir = "C:\Deploy\BodaVmsWeb"

# 1) 빌드는 기존대로
cd D:\Project\BODA.VMS.Web
dotnet publish .\BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj `
    -c Release -r win-x64 --self-contained true -o .\publish

# 2) 백업
# (§3 Step 2 그대로)

# 3) 새 위치에 복사 + 운영 설정 옮기기
New-Item -ItemType Directory -Force -Path $newDir | Out-Null
robocopy .\publish $newDir /E /NJH /NJS /NDL /NFL /NP
Copy-Item "$backup\appsettings.Production.json" $newDir -Force

# 4) 기존 서비스 삭제 + 새 경로로 재설치
Stop-Service BodaVmsWeb
sc.exe delete BodaVmsWeb
sc.exe create BodaVmsWeb binPath= "`"$newDir\BODA.VMS.Web.exe`" --environment Production" start= auto DisplayName= "BODA VMS Web Server"
sc.exe description BodaVmsWeb "BODA Vision Management System - Web Server"
sc.exe failure BodaVmsWeb reset= 86400 actions= restart/10000/restart/30000/restart/60000
Start-Service BodaVmsWeb

# 5) 검증 후 system32\publish 폴더 삭제
# (검증 OK 며칠 후 안전하게 삭제 권장)
# Remove-Item "C:\WINDOWS\system32\publish" -Recurse -Force
```

이전 후에는 본 문서 §1.1의 "운영 게시 폴더" 값을 `C:\Deploy\BodaVmsWeb` 로 갱신할 것.

---

## 8. 백업 정책

- **위치**: `C:\Backup\BodaVms-{yyyyMMdd-HHmmss}\`
- **보관 기간**: 최근 5회 또는 30일 중 짧은 것
- **정기 정리**:
  ```powershell
  # 30일 이상 된 백업 정리 (옵션)
  Get-ChildItem "C:\Backup\" -Directory -Filter "BodaVms-*" |
      Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
      Remove-Item -Recurse -Force -Confirm
  ```

---

## 9. 변경 이력

| 날짜 | 변경 | 작성자 |
|---|---|---|
| 2026-05-26 | 초안 — 예측 인프라 v1 배포 시 실제 절차 정착 | Claude Code 세션 |
| 2026-06-16 | §3 Step 5(Cloudflare 캐시 퍼지) 신설 + 체크리스트 보강 — 안돈 수정 + 스마트글라스 PoC 배포 시 | Claude Code 세션 |

---

## 10. 빠른 체크리스트 (한눈에)

배포 직전 출력해서 체크:

- [ ] 관리자 권한 PowerShell 확인 (§2.1)
- [ ] `git status` clean + 배포 대상 커밋이 master에 있음
- [ ] `Get-Service BodaVmsWeb, cloudflared` 둘 다 Running
- [ ] C: 드라이브 1GB 이상 여유
- [ ] `dotnet publish` 성공 (§3 Step 1)
- [ ] 백업 폴더 생성됨, 162MB 이상 (§3 Step 2)
- [ ] `Stop-Service` → `robocopy` → `Start-Service` 성공 (§3 Step 3)
- [ ] `http://localhost:5292/` → 200
- [ ] `https://boda-vms.com/` → 200
- [ ] (Blazor 클라이언트 변경 시) Cloudflare 캐시 퍼지 (§3 Step 5)
- [ ] 브라우저(시크릿)에서 admin 로그인 OK
- [ ] 변경한 페이지 정상 동작
- [ ] (필요 시) VMS 클라이언트 heartbeat 정상

문제 발생 시 §4 롤백 즉시 실행.
