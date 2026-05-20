# Vision Client 저장 구조

## 저장 흐름

1. **UI**: `ClientManagement.razor` 페이지에서 "Add Client" 클릭 → `ClientFormDialog`에서 Name, IP, Index 입력
2. **API**: `POST /api/clients/` 호출
3. **Service**: `ClientService.CreateClientAsync()` 실행
4. **DB 저장**: `BodaVmsDbContext`의 `Clients` 테이블(`VisionClient` 엔티티)에 INSERT
5. **동기화**: VisionServer(`http://localhost:5000`)에도 HTTP POST로 동기화 시도

## 저장되는 정보 (`VisionClient` 엔티티)

| 필드 | 설명 |
|---|---|
| `Id` | PK (자동생성) |
| `Name` | 클라이언트 이름 |
| `IpAddress` | IP 주소 |
| `ClientIndex` | 고유 인덱스 (0~99, unique) |
| `IsActive` | 활성 여부 |
| `CreatedAt` | 생성 시각 (UTC) |
| `LastSeenAt` | 마지막 하트비트 시각 |
| `HostName` | 호스트명 (하트비트로 자동 수집) |
| `SwName` | 소프트웨어 이름 (하트비트로 자동 수집) |

## DB 파일 위치

`BODA.VMS.Web/BODA.VMS.Web/BodaVision.db` (SQLite, WAL 모드)

## 관련 파일

| 파일 | 역할 |
|---|---|
| `BODA.VMS.Web/Data/Entities/Client.cs` | DB 엔티티 정의 |
| `BODA.VMS.Web/Data/BodaVmsDbContext.cs` | DbContext (Clients 테이블) |
| `BODA.VMS.Web/Endpoints/ClientEndpoints.cs` | API 엔드포인트 |
| `BODA.VMS.Web/Services/ClientService.cs` | 비즈니스 로직 |
| `BODA.VMS.Web.Client/Pages/ClientManagement.razor` | 관리 UI 페이지 |
| `BODA.VMS.Web.Client/Components/ClientFormDialog.razor` | 추가/수정 폼 |
| `BODA.VMS.Web.Client/Models/ClientDto.cs` | DTO |

## 실시간 모니터링

- `ClientMonitorService` (BackgroundService): 5초 간격으로 하트비트 확인
- 15초 이상 하트비트 없으면 Offline 판정
- SignalR (`/hubs/vms`)로 `ClientStatusChanged` 이벤트 브로드캐스트
