# BODA.VMS.Web ↔ VMS 통합 작업 계획서

> **읽는 대상**: 양쪽 솔루션의 Claude Code 에이전트
> **마지막 수정**: 2026-05-21
> **공동 목표**: VMS(.NET 8 WPF 비전 클라이언트) ↔ BODA.VMS.Web(.NET 8 Blazor) 실제 운영 통합

---

## 1. 시스템 개요

| 항목 | VMS | BODA.VMS.Web |
|---|---|---|
| 저장소 | https://github.com/j2ase1862/VMS.git | https://github.com/j2ase1862/BODA.VMS.Web.git |
| 위치 | (별도 워크스페이스) | `D:\Project\BODA.VMS.Web` |
| 플랫폼 | .NET 8 WPF (`net8.0-windows7.0`) | .NET 8 Blazor WASM + ASP.NET Core |
| 역할 | **실시간 비전 검사 실행** | **MES (지시/SPC/OEE/감사/리포트)** |
| 카메라/PLC | 직접 제어 | — |
| 레시피 마스터 | 다운로드만 | **단일 진실 원본** |
| 검사 결과 | 임시 → Web 업로드 | **영구 저장 + 통계** |
| 사용자 인증 | 로컬 BCrypt | JWT |
| 데이터베이스 | 로컬 SQLite (사용자만) | SQLite (`C:\ProgramData\BODA\VMS\BodaVision.db`) |

**중요 정정** (이전 가정 무효):
- ❌ VMS는 .NET Framework 4.8.1이 아님 → ✅ .NET 8 WPF
- ❌ 공유 SQLite DB 사용 안 함 → ✅ HTTP REST API 단방향 동기화
- ❌ Cognex VisionPro 의존 → ✅ OpenCvSharp4 + NativeVision C++ DLL

---

## 2. 통합 contract (현재 매칭 상태)

VMS의 `VMS.Core/Services/ParameterSyncService.cs` 및 `HeartbeatService.cs`가 호출하는 6개 엔드포인트.

| # | Direction | HTTP | URL | Body / Query | Response | 호출 주기 | 매칭 |
|---|---|---|---|---|---|---|---|
| 1 | VMS → Web | `GET` | `/api/parameters/sync/recipes/{clientIndex}` | path | `List<RecipeSummaryDto>` | 60s timer + 부팅 | ✅ |
| 2 | VMS → Web | `GET` | `/api/parameters/sync/recipe/{recipeId}` | path | `List<RecipeParameterDto>` (IsActive만) | 사용자 선택 시 | ✅ |
| 3 | VMS → Web | `POST` | `/api/parameters/results` | `ParameterResultUploadRequest` | `{historyId, isPass}` | 검사 1회 종료 시 | ⚠️ wire OK / 추적성 필드 미사용 |
| 4 | VMS → Web | `POST` | `/api/clients/heartbeat` | `{clientIndex, hostName, swName}` | 200 / 404 | **5초 주기** | ✅ |
| 5 | VMS → Web | `POST` | `/api/clients/disconnect` | `{clientIndex}` | 200 / 404 | 종료 시 (2s 타임아웃) | ✅ |
| 6 | VMS → VisionServer (별도) | `POST` | `/api/v1/Clients` | `{Name, IpAddress, Index}` (PascalCase) | 200 | heartbeat 404 시 1회 자동등록 | Web 책임 외 |

**결론**: 와이어 contract는 사실상 100% 정렬되어 있음. 새로 만들 엔드포인트 없음.

---

## 3. DTO 정의 (양쪽이 일치해야 함)

### 3.1 `RecipeSummaryDto`
```csharp
public class RecipeSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}
```

### 3.2 `RecipeParameterDto`
```csharp
public class RecipeParameterDto
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int ParamCode { get; set; }
    public double ParamValue { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Dimension"; // Pattern | Blob | Dimension
    public string? Unit { get; set; }
    public bool IsActive { get; set; }
    // Web에만 존재 (VMS는 무시) — 향후 SPC 판정용
    public double? LowerLimit { get; set; }
    public double? UpperLimit { get; set; }
}
```

### 3.3 `ParameterResultDto`
```csharp
public class ParameterResultDto
{
    public int ParamCode { get; set; }
    public double MeasuredValue { get; set; }
    public string Judgment { get; set; } = "OK"; // "OK" or NG code
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

### 3.4 `ParameterResultUploadRequest` ⚠️ (양쪽 차이 있음)
```csharp
public class ParameterResultUploadRequest
{
    public int ClientIndex { get; set; }
    public int RecipeId { get; set; }
    public List<ParameterResultDto> Results { get; set; } = new();

    // ─── Web만 보유, VMS 미사용 (작업 항목 #3) ───
    public int? WorkOrderId { get; set; }
    public int? LotId { get; set; }
    public int? OperatorId { get; set; }
    public string? SerialNumber { get; set; }
}
```

### 3.5 Heartbeat / Disconnect
```csharp
// Heartbeat
public class HeartbeatRequest
{
    public int ClientIndex { get; set; }
    public string? HostName { get; set; }
    public string? SwName { get; set; }
}

// Disconnect
public class DisconnectRequest
{
    public int ClientIndex { get; set; }
}
```

**JSON 직렬화 규약**: CamelCase (양쪽 .NET 8 ASP.NET Core 기본값). `PropertyNamingPolicy = null`로 변경 금지.

---

## 4. 작업 항목 (양 팀 공동)

### Phase 1 — E2E 연결 검증 (수정 없음, 30분)

| 작업자 | 절차 |
|---|---|
| Web | `dotnet run --project BODA.VMS.Web/BODA.VMS.Web/BODA.VMS.Web.csproj --launch-profile https` → `https://localhost:7144` 대기 |
| VMS | `appsettings.json` 또는 환경설정의 `WebServerUrl = "https://localhost:7144"`, `ClientIndex = 1` |
| VMS | 실행 → 5초 후 첫 heartbeat 전송 |
| Web | 콘솔 로그에서 `Heartbeat OK from index 1` 확인 |
| Web | `/clients` 페이지에서 클라이언트가 online 표시되는지 확인 |
| VMS | 종료 시 `/api/clients/disconnect` 호출 확인 |

**Pass 기준**: heartbeat 200 응답 + Web UI에 online dot.

### Phase 2 — 레시피 동기화 + 결과 업로드 검증 (수정 없음, 1시간)

| 작업자 | 절차 |
|---|---|
| Web | UI에서 ClientIndex=1인 Client 추가 (또는 자동등록 후 확인) |
| Web | `/inspection-items`에서 새 레시피 생성 → 파라미터 2~3개 등록 (`ParamCode=1` "패턴1 점수", `ParamCode=2` "치수 폭" 등) |
| VMS | `ParameterSyncService.SyncRecipesAsync` 자동 호출 대기 (≤60s) 또는 수동 트리거 |
| VMS | UI에서 새 레시피가 보이는지 확인 |
| VMS | 검사 시뮬레이션 → `UploadResultsAsync` 호출 |
| Web | `/history` 페이지에서 새 `InspectionHistory` 행 확인 |
| Web | `/quality-analysis` 또는 SQL로 `ParameterMeasurement` 테이블에 row 확인 |
| Web | NG 결과인 경우 `/alarms` 페이지에서 `AlarmEvent` 생성 확인 |

**Pass 기준**: 결과 row가 DB에 들어가고 알람이 생성됨.

### Phase 3 — VMS에 추적성 4필드 추가 (VMS 측 작업, 1~2시간)

**책임**: VMS 측 Claude Code 에이전트

**변경 파일**: `VMS.Core/Models/ParameterResultUploadRequest.cs` (또는 동등 위치)

**추가할 필드**:
```csharp
public int? WorkOrderId { get; set; }
public int? LotId { get; set; }
public int? OperatorId { get; set; }
public string? SerialNumber { get; set; }
```

**런타임 채우는 곳**: 검사 결과를 만드는 ViewModel (예: `MainWindow` 또는 `InspectionViewModel`).

- `WorkOrderId` — 사용자가 시작 화면에서 선택한 작업지시 ID. 미선택이면 null.
- `LotId` — 작업지시의 활성 Lot ID. UI에 "현재 Lot" 표시 + 자동/수동 전환.
- `OperatorId` — 출근한 작업자 ID. 키오스크 또는 PIN 입력 시 저장.
- `SerialNumber` — 바코드/QR 스캔으로 얻은 시리얼. 없으면 null.

**상세 옵션**: VMS UI에 다음 컨텍스트 선택 영역 신설
1. **상단 status bar**: `WO: WO-20260521-001 / Lot: 03 / Operator: 김철수 (KCS-001)`
2. **시리얼 입력**: 키보드/스캐너 입력 필드 (검사 전 스캔)
3. **출퇴근**: VMS 자체 키오스크 화면 (Web의 키오스크와 별개) 또는 Web `/kiosk/{idx}` 사용

**테스트**: 검사 결과 업로드 시 Web의 `InspectionHistory` row에 `WorkOrderId/LotId/OperatorId/SerialNumber` 컬럼이 채워지는지 확인.

**Pass 기준**: Web의 OEE 페이지에서 작업지시별/Lot별/작업자별 통계가 분리되어 보임.

### Phase 4 — Self-register Fallback (선택, Web 측 작업, 30분)

**책임**: Web 측 Claude Code 에이전트

VisionServer가 없을 때(현재 `VisionServer:Enabled = false`) VMS가 처음 heartbeat → 404 → VisionServer 등록 시도 실패 → 영구히 등록 불가 상태.

**해결**: Web에 익명 self-register 엔드포인트 추가.

**위치**: `BODA.VMS.Web/Endpoints/ClientEndpoints.cs`

```csharp
group.MapPost("/register", async (
    [FromBody] ClientRegisterRequest req,
    IClientService svc) =>
{
    if (req.ClientIndex < 0) return Results.BadRequest();
    var existing = await svc.GetByIndexAsync(req.ClientIndex);
    if (existing is not null) return Results.Ok(existing);
    var dto = new ClientDto
    {
        ClientIndex = req.ClientIndex,
        Name = req.Name ?? $"Client #{req.ClientIndex:D2}",
        IpAddress = req.IpAddress ?? "",
        IsActive = true
    };
    var created = await svc.CreateAsync(dto);
    return Results.Ok(created);
}).AllowAnonymous();

public record ClientRegisterRequest(int ClientIndex, string? Name, string? IpAddress);
```

VMS 측: `HeartbeatService.EnsureClientRegisteredAsync`에서 VisionServer가 비활성이거나 404일 때 fallback으로 `POST /api/clients/register` 호출.

**Pass 기준**: VisionServer 꺼진 상태에서 새 VMS 실행 → 첫 heartbeat 후 Web에 자동 등록 → 이후 heartbeat 정상.

### Phase 5 — NG 알람 SignalR Push 검증 (수정 없음, 30분)

| 작업자 | 절차 |
|---|---|
| Web | `/alarms` 페이지 열어둔 채로 |
| VMS | NG 판정이 포함된 결과 업로드 |
| Web | SignalR `/hubs/vms`의 `AlarmCreated` 이벤트가 즉시 도착하는지 확인 (페이지에 새 row가 자동 추가) |
| Web | `NotificationBell`이 카운트 증가하는지 확인 |

**Pass 기준**: 새로고침 없이 알람이 실시간 표시됨.

### Phase 6 — 레시피 변경 전파 검증 (수정 없음, 15분)

| 작업자 | 절차 |
|---|---|
| Web | `/inspection-items`에서 레시피 추가 (예: "TestRecipe2") |
| VMS | 최대 60초 대기 (다음 `SyncRecipesAsync` 사이클) |
| VMS | UI 레시피 드롭다운에 새 레시피 표시 확인 |
| Web | 기존 레시피 삭제 |
| VMS | 60초 내 드롭다운에서 사라지는지 확인 |

**Pass 기준**: 60초 이내 양방향 반영. `RecipeListChanged` 이벤트 정상.

---

## 5. 작업 분담 표

| Phase | Web 측 작업 | VMS 측 작업 | 동시 진행 가능? |
|---|---|---|---|
| 1 | 서버 실행 + 검증 | VMS 설정 + 실행 | ✅ |
| 2 | 레시피 시드 + DB 확인 | UI에서 결과 트리거 | ✅ |
| 3 | (Web의 `ParameterResultUploadRequest` 그대로 유지) | **DTO 4필드 추가 + UI 컨텍스트 신설** | VMS만 |
| 4 | **`/api/clients/register` 추가** | `HeartbeatService` fallback 로직 추가 | 순차 |
| 5 | UI 모니터 | NG 결과 발생시키기 | ✅ |
| 6 | 레시피 CRUD | 드롭다운 관찰 | ✅ |

---

## 6. 주의사항 / 합의 사항

1. **익명 엔드포인트 유지** — 위 6개 엔드포인트 모두 `.AllowAnonymous()`. JWT 전역화 금지. 각 엔드포인트가 명시적으로 anonymous임을 보장.

2. **JSON CamelCase** — 양쪽 ASP.NET Core 8 기본값 사용. `AddJsonOptions`에서 `PropertyNamingPolicy = null`로 변경 금지.

3. **HTTPS 인증서** — 개발 환경에서 VMS가 `https://localhost:7144`의 self-signed 인증서를 trust해야 함. 옵션:
   - Windows에서 `dotnet dev-certs https --trust`
   - 또는 VMS의 `HttpClientHandler.ServerCertificateCustomValidationCallback`을 dev 모드에서 우회
   - 운영에서는 사설 CA 또는 HTTP

4. **ClientIndex 유일성** — 동일 ClientIndex로 두 VMS 인스턴스 실행 금지 (Web이 IP 덮어쓰기 + 경고만 함). 운영 가이드에 명시 필요.

5. **Judgment 문자열 규약** — 양쪽 `"OK"` 정확히 일치 (대소문자/공백 X). NG 시는 실제 NG 코드 (예: `"P-001"`).

6. **타임존** — `Timestamp`/`OccurredAt` 등 모두 UTC로 송수신. 표시는 클라이언트에서 `ToLocalTime()`.

7. **양 팀 동기화** — DTO 변경이 필요한 경우 (Phase 3 외 추가가 있다면) 본 문서를 먼저 업데이트한 뒤 양쪽 구현.

8. **장기적 ShareLibrary** — 양쪽 모두 .NET 8이라 `BODA.VMS.ShareLibrary` (.NET Standard 2.0 → net8.0) 신설해서 DTO를 공유하면 drift 방지에 좋음. 우선순위는 낮음.

---

## 7. 진행 상태 (체크리스트)

각 항목 완료 시 이 문서를 PR로 업데이트.

- [ ] **Phase 1** — E2E 연결 (heartbeat + disconnect) — 사용자 검증 필요
- [ ] **Phase 2** — 레시피 동기화 + 결과 업로드 + InspectionHistory/ParameterMeasurement/AlarmEvent 저장 확인 — 사용자 검증 필요
- [x] **Phase 3 (DTO)** — VMS `ParameterResultUploadRequest`에 추적성 4필드 추가 (2026-05-21, VMS 에이전트)
- [x] **Phase 3 (UI)** — VMS sub-toolbar 에 Inspection Context inline 입력 카드 (WO/Lot/Op ID + Serial + Clear) 추가, ParameterSyncService 와 양방향 sync (2026-05-21, VMS 에이전트)
- [x] **Phase 4 (Web)** — `/api/clients/register` 익명 엔드포인트 + ClientRegisterRequest record (2026-05-21, VMS 에이전트가 양쪽 작성)
- [x] **Phase 4 (VMS)** — `HeartbeatService` VisionServer 실패 시 Web self-register 호출 fallback (2026-05-21, VMS 에이전트)
- [ ] **Phase 5** — NG 알람 SignalR 실시간 푸시 검증 — 사용자 검증 필요
- [ ] **Phase 6** — 레시피 변경 60초 내 전파 검증 — 사용자 검증 필요
- [ ] **(선택)** ShareLibrary 신설 + DTO 이관 — ShareLibrary 현재 .NET Framework 4.8 + Cognex 의존이라 .NET 8 sub-project 신설 필요 (큰 작업)

---

## 8. 참고 파일 (절대 경로)

**Web 측 (`D:\Project\BODA.VMS.Web`)**
- `BODA.VMS.Web/BODA.VMS.Web/Endpoints/ClientEndpoints.cs` — heartbeat / disconnect (+ Phase 4의 register 추가 예정)
- `BODA.VMS.Web/BODA.VMS.Web/Endpoints/InspectionItemEndpoints.cs` — sync/recipes, sync/recipe, /results
- `BODA.VMS.Web/BODA.VMS.Web.Client/Models/InspectionItemDto.cs` — DTO 정의
- `BODA.VMS.Web/BODA.VMS.Web.Client/Models/ClientDto.cs`
- `BODA.VMS.Web/BODA.VMS.Web/Data/Entities/InspectionHistory.cs` — 결과 저장 엔티티
- `BODA.VMS.Web/BODA.VMS.Web/Data/Entities/ParameterMeasurement.cs` — SPC용 분리 저장
- `BODA.VMS.Web/BODA.VMS.Web/Hubs/VmsHub.cs` — SignalR (AlarmCreated/Updated 푸시)

**VMS 측** (참조용 — GitHub raw)
- `VMS.Core/Services/ParameterSyncService.cs`
- `VMS.Core/Services/HeartbeatService.cs`
- `VMS.Core/Models/` 또는 `VMS.Core/Dtos/` (DTO 위치)

---

## 9. 변경 이력

| 날짜 | 작성자 | 내용 |
|---|---|---|
| 2026-05-21 | Web 측 | 최초 작성 — 매칭 분석, Phase 1~6 정의 |
| 2026-05-21 | VMS 측 | Phase 3 (DTO 4필드) + Phase 4 (Web register + VMS fallback) 구현. VMS는 OpenCvSharp/.NET 8이라 ShareLibrary 무관함을 명확화. VMS 측 테스트 121/121 통과, Web CS 에러 0 (dev 서버 lock 외에는 클린). |
| 2026-05-21 | VMS 측 | Phase 3 UI 추가 — MainView sub-toolbar 에 Inspection Context inline 입력 (WO/Lot/Op ID + Serial + Clear). 값 변경 시 ParameterSyncService 의 setter 가 호출되어 다음 UploadResultsAsync 부터 자동 첨부됨. |
