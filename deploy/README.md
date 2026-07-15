# BODA.VMS.Web 배포

Kestrel 단독 + Windows Service 방식. IIS 불필요.

## 설치

```powershell
# 관리자 PowerShell
cd D:\Project\BODA.VMS.Web\deploy
.\Install-Web.ps1
```

기본값:
- 설치 폴더: `C:\Program Files\BODA VMS Web`
- 포트: 7144 (HTTPS, self-signed)
- 서비스 이름: `BODA-VMS-Web`
- 자동 시작: 부팅 시 자동 (실패 시 5초 후 자동 재시작 3회)

옵션 변경 예:
```powershell
.\Install-Web.ps1 -Port 8443 -InstallPath "D:\BODA Web"
```

설치 후:
- 브라우저: `https://localhost:7144`
- 서비스 상태 확인: `Get-Service BODA-VMS-Web`
- 로그 확인: Event Viewer → Windows Logs → Application

## 제거

```powershell
.\Uninstall-Web.ps1
```

서비스 + 방화벽 규칙 + 설치 폴더 제거. **SQLite DB 는 보존**
(`$env:LOCALAPPDATA\BODA\VMS\BodaVision.db`) — 재설치 시 데이터 유지.

완전 제거 (DB 포함):
```powershell
.\Uninstall-Web.ps1
Remove-Item "$env:LOCALAPPDATA\BODA\VMS" -Recurse
```

## admin 비밀번호 분실 / 로그인 401

**`Reset-AdminPassword.cmd` 더블클릭** → UAC 승인 → 새 비밀번호 입력. 끝.
설정 기록 → 서비스 재시작 → 잠금 해제 → 새 비밀번호 로그인 확인까지 자동으로
진행되며 운영 DB 는 보존된다 (감사 로그에 `AdminPasswordReset` 기록).

원인 진단이 먼저 필요하면 `Diagnose-AdminLogin.ps1` (DB 감사 로그로 401 사유 특정).
배경: `Initial.AdminPassword` 는 첫 부팅 시드에만 쓰이므로, 계정이 생긴 뒤
설정 파일을 고쳐도 비밀번호는 바뀌지 않는다 — 그때 이 도구를 쓴다.

## 트러블슈팅

**서비스가 시작 안 됨**: Event Viewer → Application → BODA-VMS-Web 로그 확인.
주로 포트 충돌 또는 HTTPS 인증서 문제.

**HTTPS 인증서 에러**: dev 환경에서
```powershell
dotnet dev-certs https --trust
```

**다른 포트 사용 중**: `netstat -ano | findstr 7144` 로 점유 프로세스 확인 후
다른 포트로 재설치 (`Install-Web.ps1 -Port 8443`).

**VMS 클라이언트에서 접속 실패**: 방화벽 규칙 확인 + VMS 의
`appsettings.json` 에서 `WebServerUrl` 이 올바른 IP/포트를 가리키는지.

## 업그레이드 (v1.1 → v1.2 등)

```powershell
# DB 유지하면서 재설치
.\Install-Web.ps1
```

스크립트가 자동으로 기존 서비스 중지 → 폴더 갱신 → 서비스 재시작.
DB 는 LocalAppData 에 있어 영향 없음.
