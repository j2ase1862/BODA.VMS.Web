# BODA.VMS.Web 배포 가이드

## 1. 개요

BODA VMS Web Server는 산업용 비전 검사 시스템의 웹 관리 서버입니다. 이 문서는 서버의 빌드, 게시, 배포 및 Windows 서비스 등록 방법을 안내합니다.

| 항목 | 내용 |
|------|------|
| 프레임워크 | .NET 8.0 (ASP.NET Core + Blazor WebAssembly) |
| 대상 플랫폼 | Windows 10 / Windows Server 2016 이상 (x64) |
| 배포 방식 | Self-contained (대상 서버에 .NET 설치 불필요) |
| 데이터베이스 | SQLite (BodaVision.db) |
| 기본 포트 | 5292 (HTTP) |
| 인증 | JWT Bearer Token |

---

## 2. 사전 요구사항

### 빌드 환경 (개발 PC)

- .NET SDK 8.0 이상 설치
- PowerShell 5.1 이상

### 배포 대상 서버

- Windows 10 / Windows Server 2016 이상 (x64)
- .NET 런타임 설치 **불필요** (self-contained 배포)
- 방화벽에서 포트 5292 허용 필요

---

## 3. 빌드 및 게시

### 3.1 PowerShell 스크립트 사용 (권장)

솔루션 루트 디렉토리(`BODA.VMS.Web/`)에서 실행합니다.

```powershell
# 게시만 수행 (.\publish 폴더에 출력)
.\publish.ps1

# 게시 출력 디렉토리 지정
.\publish.ps1 -PublishDir "C:\Deploy\BodaVmsWeb"
```

### 3.2 수동 게시 (dotnet CLI)

```powershell
dotnet publish BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o .\publish
```

### 3.3 게시 결과물

게시가 완료되면 출력 폴더에 다음 파일들이 생성됩니다.

```
publish/
├── BODA.VMS.Web.exe            ← 실행 파일
├── BODA.VMS.Web.dll
├── appsettings.json            ← 기본 설정
├── appsettings.Production.json ← Production 환경 설정
├── web.config
├── wwwroot/                    ← 정적 파일 (Blazor WASM 포함)
└── (기타 .dll, .json 파일들)
```

전체 크기: 약 **156MB** (.NET 런타임 포함)

---

## 4. 배포 전 설정

### 4.1 JWT 보안 키 변경 (필수)

`appsettings.Production.json` 파일에서 JWT 키를 반드시 변경해야 합니다.

```json
{
  "Jwt": {
    "Key": "여기에-32자-이상의-안전한-랜덤-문자열-입력"
  }
}
```

> **주의:** 기본 키(`CHANGE-THIS-TO-A-SECURE-RANDOM-KEY-AT-LEAST-32-CHARS`)를 그대로 사용하면 보안 위험이 있습니다. 반드시 고유한 값으로 변경하세요.

### 4.2 포트 변경 (선택)

기본 포트(5292)를 변경하려면 `appsettings.Production.json`을 수정합니다.

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:원하는포트"
      }
    }
  }
}
```

### 4.3 데이터베이스 경로 변경 (선택)

기본적으로 실행 파일과 같은 경로에 `BodaVision.db`가 생성됩니다. 경로를 변경하려면:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=C:\\Data\\BodaVision.db"
  }
}
```

### 4.4 Heartbeat 모니터링 설정 (선택)

비전 클라이언트 상태 감지 주기를 조정할 수 있습니다.

```json
{
  "ClientMonitor": {
    "HeartbeatTimeoutSeconds": 15,
    "CheckIntervalSeconds": 5
  }
}
```

| 설정 | 기본값 | 설명 |
|------|--------|------|
| `HeartbeatTimeoutSeconds` | 15 | 마지막 heartbeat 이후 이 시간이 지나면 offline 판정 |
| `CheckIntervalSeconds` | 5 | 상태 확인 주기 |

---

## 5. 실행 방법

### 5.1 콘솔 모드 (테스트/디버깅용)

게시 폴더에서 직접 실행합니다.

```powershell
# 기본 실행 (Development 환경)
.\BODA.VMS.Web.exe

# Production 환경으로 실행
.\BODA.VMS.Web.exe --environment Production

# 특정 URL로 실행
.\BODA.VMS.Web.exe --urls "http://0.0.0.0:8080"
```

실행 후 브라우저에서 `http://localhost:5292`에 접속합니다.

> Ctrl+C로 종료합니다.

### 5.2 Windows 서비스 (운영 환경 권장)

Windows 서비스로 등록하면 시스템 부팅 시 자동 시작되고, 장애 시 자동 복구됩니다.

#### 자동 설치 (PowerShell 스크립트)

```powershell
# 관리자 권한 PowerShell에서 실행
.\publish.ps1 -Install
```

#### 수동 설치 (sc.exe)

```powershell
# 관리자 권한 PowerShell에서 실행

# 서비스 생성
sc.exe create BodaVmsWeb `
    binPath= """C:\Deploy\BodaVmsWeb\BODA.VMS.Web.exe"" --environment Production" `
    start= auto `
    DisplayName= "BODA VMS Web Server"

# 서비스 설명 설정
sc.exe description BodaVmsWeb "BODA Vision Management System - Web Server"

# 장애 복구 정책 설정 (10초 / 30초 / 60초 후 재시작)
sc.exe failure BodaVmsWeb reset= 86400 actions= restart/10000/restart/30000/restart/60000

# 서비스 시작
net start BodaVmsWeb
```

#### 서비스 관리

```powershell
# 상태 확인
Get-Service BodaVmsWeb

# 서비스 중지
Stop-Service BodaVmsWeb

# 서비스 시작
Start-Service BodaVmsWeb

# 서비스 제거
.\publish.ps1 -Uninstall
# 또는 수동으로:
net stop BodaVmsWeb
sc.exe delete BodaVmsWeb
```

> Services.msc (Windows 서비스 관리자)에서도 확인/관리할 수 있습니다.

---

## 6. Windows 서비스 설정 요약

| 항목 | 값 |
|------|-----|
| 서비스 이름 | `BodaVmsWeb` |
| 표시 이름 | BODA VMS Web Server |
| 시작 유형 | 자동 |
| 실행 인수 | `--environment Production` |
| 복구 (1차) | 10초 후 재시작 |
| 복구 (2차) | 30초 후 재시작 |
| 복구 (3차) | 60초 후 재시작 |
| 카운터 리셋 | 24시간 |

---

## 7. 설정 파일 참조

### appsettings.Production.json (전체)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=BodaVision.db"
  },
  "Jwt": {
    "Key": "여기에-32자-이상의-안전한-랜덤-문자열-입력",
    "Issuer": "BODA.VMS.Web",
    "Audience": "BODA.VMS.Web.Client",
    "ExpireMinutes": 480
  },
  "ClientMonitor": {
    "HeartbeatTimeoutSeconds": 15,
    "CheckIntervalSeconds": 5
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5292"
      }
    }
  }
}
```

---

## 8. 업데이트 절차

기존에 운영 중인 서버를 업데이트할 때:

1. **서비스 중지**
   ```powershell
   Stop-Service BodaVmsWeb
   ```

2. **기존 파일 백업**
   ```powershell
   # 설정 파일과 DB 백업
   Copy-Item "C:\Deploy\BodaVmsWeb\appsettings.Production.json" "C:\Backup\"
   Copy-Item "C:\Deploy\BodaVmsWeb\BodaVision.db" "C:\Backup\"
   ```

3. **새 빌드 게시**
   ```powershell
   .\publish.ps1 -PublishDir "C:\Deploy\BodaVmsWeb"
   ```

4. **설정 파일 복원** (게시 시 덮어쓴 경우)
   ```powershell
   Copy-Item "C:\Backup\appsettings.Production.json" "C:\Deploy\BodaVmsWeb\"
   ```

5. **서비스 시작**
   ```powershell
   Start-Service BodaVmsWeb
   ```

> **참고:** SQLite 데이터베이스(`BodaVision.db`)는 게시 결과물에 포함되지 않으므로 덮어쓰기 걱정이 없습니다. 단, 안전을 위해 백업을 권장합니다.

---

## 9. 문제 해결

### 서비스가 시작되지 않는 경우

1. **이벤트 뷰어 확인**: Windows 이벤트 뷰어 > 응용 프로그램 로그에서 오류 확인
2. **콘솔 모드 테스트**: 서비스를 중지하고 exe를 직접 실행하여 오류 메시지 확인
   ```powershell
   Stop-Service BodaVmsWeb
   cd C:\Deploy\BodaVmsWeb
   .\BODA.VMS.Web.exe --environment Production
   ```
3. **포트 충돌**: 5292 포트가 이미 사용 중인지 확인
   ```powershell
   netstat -ano | findstr :5292
   ```

### 비전 클라이언트가 연결되지 않는 경우

1. 방화벽에서 포트 5292가 허용되어 있는지 확인
2. 비전 클라이언트의 `settings.json`에서 `WebServerUrl`이 올바른지 확인
   ```json
   {
     "Environment": {
       "WebServerUrl": "http://서버IP:5292"
     }
   }
   ```
3. 서버에서 heartbeat 로그 확인 (Logging 수준을 Information으로 변경)

### 브라우저 접속 불가

1. 서버 로컬에서 `http://localhost:5292` 접속 테스트
2. 원격 접속 시 `http://서버IP:5292` 사용
3. Kestrel이 `0.0.0.0`에 바인딩되어 있는지 확인 (외부 접근 허용)

---

## 10. 기본 계정 정보

| 항목 | 값 |
|------|-----|
| 관리자 ID | `admin` |
| 관리자 비밀번호 | `admin` |

> **주의:** 첫 배포 후 반드시 관리자 비밀번호를 변경하세요.
