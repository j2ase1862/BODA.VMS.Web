# BODA.VMS.Web GS 인증 Baseline — 취약점 감사 및 조치 보고서

**문서 버전**: 1.2
**작성일**: 2026-06-02 (v1.0) / 2026-06-04 (v1.1) / 2026-06-10 (v1.2)
**대상**: BODA.VMS.Web v1.0 (Critical+High baseline) → v1.1 (잔여 7 항목) → v1.2 (인증 강화)
**기준**: 한국 TTA GS(Good Software) 인증, ISO/IEC 25023 (SQuaRE) 8 개 품질 특성

---

## 1. 개요

BODA.VMS.Web 솔루션(ASP.NET Core 8 + Blazor WebAssembly, VMS 데스크탑과 SQLite DB 공유) 의 GS 인증 신청 가능 baseline 을 구축하고, 후속으로 baseline 외 잔여 항목 + 인증(authn) 강화까지 모두 처리. 사전 감사로 식별한 **Critical 4 + High 4 + 잔여 7 + 인증 강화 6 = 총 21 항목** 모두 처리 완료.

| 카테고리 | 항목 수 | 완료 | 비고 |
|----------|---------|------|------|
| Critical (탈락 위험) | 4 | ✅ 4/4 | v1.0 |
| High (지적 가능성) | 4 | ✅ 4/4 | v1.0 |
| 잔여 (baseline 외, GS 강화) | 7 | ✅ 7/7 | v1.1 |
| 인증 강화 (brute-force/패스워드/감사/CSP) | 6 | ✅ 6/6 | v1.2 |
| **자동 테스트** | **443 통과 / 0 실패** | | 시작 9 → 종료 443 (49 배 증가) |

본 문서는 각 항목의 **취약점 → 영향 → 조치 → 검증** 흐름을 기록.

---

## 2. 사전 감사 평가표 (Before, 2026-06-02)

GS 8 개 품질 특성 기준 사전 진단:

| 품질 특성 | 평가 | 핵심 갭 |
|-----------|------|--------|
| 기능 적합성 | 중 | 입력 검증 미흡 (DataAnnotations 만, FluentValidation 부재) |
| **신뢰성** | **낮음** | try-catch 62 회 분산, 글로벌 예외 처리 부재, 헬스 체크 미구현 |
| **보안성** | **매우낮음** | **JWT Key 평문 저장**, CORS 미설정, 보안 헤더 누락, 23 익명 endpoint 접근 제어 없음 |
| 성능 효율성 | 중상 | async/await 일관, Include() 사용 양호 |
| 호환성 | 중 | API versioning / Swagger 미구현 |
| 사용성 | 중상 | Blazor WASM + MudBlazor, 다국어 12 종 적용 |
| 유지보수성 | 중 | **단위 테스트 0 개**, 코드 품질 도구 미구현 |
| 이식성 | 중상 | DEPLOYMENT_GUIDE 상세, Windows Service 자동화 |

---

## 3. Critical 4 항목 — 인증 탈락 위험 우선 조치 (v1.0)

### 3.1 JWT Key 평문 저장
**취약점**
- `appsettings.json:21` 의 `Jwt:Key` 가 소스에 평문 저장 (`"BodaVms2025SuperSecretKeyForJwtTokenGeneration!"`)
- `appsettings.Production.json:13` 운영 키도 동일 패턴
- Git 히스토리에 영구 노출, 키 회전 불가

**영향**
- 비밀 노출 → 토큰 위조 가능 → 전체 인증 우회
- GS 인증 보안성 항목 즉시 탈락 사유

**조치 (PR #5)**
- `BODA.VMS.Web.csproj` 에 `<UserSecretsId>` 추가 → `dotnet user-secrets` 활성
- `appsettings.json` / `appsettings.Production.json` 에서 `Jwt:Key` 라인 **삭제**
- `Program.cs` 시작 시 검증: Key 부재 또는 32 자 미만이면 `InvalidOperationException` 으로 **부팅 중단** + 명확한 메시지 (개발/운영 설정 가이드 포함)
- `DEPLOYMENT_GUIDE.md` 4.1 섹션 재작성 — user-secrets / 환경변수 가이드 + 64자 키 생성 PowerShell 예시

**검증**
- ✅ 빌드 시 git diff 에 키 노출 없음
- ✅ Key 없이 `dotnet run` 시 명확한 메시지로 부팅 중단
- ✅ `dotnet user-secrets set "Jwt:Key" "<32자>"` 후 정상 부팅

**운영 절차**
```powershell
# 개발
dotnet user-secrets set "Jwt:Key" "<32자 이상 무작위 키>" --project BODA.VMS.Web\BODA.VMS.Web

# 운영 (관리자 PowerShell)
[Environment]::SetEnvironmentVariable("Jwt__Key", "<32자 이상>", "Machine")
```

---

### 3.2 글로벌 예외 처리 부재
**취약점**
- `Program.cs:774` 의 `UseExceptionHandler("/Error")` 는 Razor 페이지 전용
- 23 개 API endpoint 의 unhandled exception 이 default ASP.NET 처리로 빠짐 → 클라이언트가 stack trace 노출되거나 raw 500 만 받음
- 분산된 try-catch 62 회 — 일관성 없음, 로깅 누락 위험

**영향**
- 정보 노출 (stack trace, 내부 경로)
- 장애 분석 어려움 (구조화 로깅 부재)
- GS 신뢰성 항목 지적 사유

**조치 (PR #5)**
- 신규 `Middleware/ApiExceptionHandler.cs` — .NET 8 `IExceptionHandler` 패턴
- `/api/`, `/hubs/`, `/auth/`, `/admin/` 경로의 unhandled exception → **RFC 7807 ProblemDetails JSON** 반환
- `ILogger` 구조화 로깅 + `traceId` 자동 포함
- 개발 환경: Detail 에 full stack trace / 운영: sanitized 메시지
- Razor 페이지 요청은 기존 `/Error` fallback (`UseExceptionHandler` options 의 `ExceptionHandlingPath`) — `TryHandleAsync` 가 false 반환해 위임

**검증**
- ✅ API endpoint 의도적 throw → JSON ProblemDetails + traceId 응답
- ✅ Razor 페이지 throw → `/Error` 페이지 (기존 동작 보존)

---

### 3.3 보안 헤더 + CORS 누락
**취약점**
- `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy` 모두 미설정
- **`Content-Security-Policy` 미적용** — XSS / 데이터 유출 방어 표준 누락 (v1.1 에서 후속 처리, §5.5 참조)
- CORS 정책 부재 → cross-origin 동작 예측 불가

**영향**
- Clickjacking, MIME 스니핑, referrer 정보 노출 등 OWASP top 10 위협 노출
- GS 보안성 항목 baseline 미달

**조치 (PR #5)**
- 신규 `Middleware/SecurityHeadersMiddleware.cs` — 모든 응답에 baseline 헤더:
  | 헤더 | 값 | 목적 |
  |------|-----|------|
  | `X-Frame-Options` | `SAMEORIGIN` | Clickjacking 방어 |
  | `X-Content-Type-Options` | `nosniff` | MIME 스니핑 방어 |
  | `Referrer-Policy` | `strict-origin-when-cross-origin` | Referrer 노출 최소화 |
  | `Permissions-Policy` | `camera=()/microphone=()/geolocation=()/payment=()` | 권한 최소화 |
- CORS: `appsettings.Cors:AllowedOrigins` 배열 — 비어있으면 **cross-origin 차단** (same-origin 만 허용)
- CSP 는 Blazor WASM 의 `wasm-unsafe-eval` 호환성 검토 필요로 v1.1 분리 (§5.5)

**검증**
- ✅ 브라우저 DevTools 응답 헤더에 4 헤더 모두 존재 확인

---

### 3.4 입력 검증 미흡 (FluentValidation 28 Validator)
**취약점**
- DTO 검증이 일부 `DataAnnotations` 만 — 복잡 규칙(정규식, 조건부, 컬렉션 항목, 범위) 미적용
- 23 endpoint 의 약 32 개 DTO 중 검증 누락 다수
- 회원가입 암호 정책: `MinLength=4` — 너무 약함
- 키오스크 PIN: 형식 검증 부재 (4-8 자리 숫자 정책 없음)

**영향**
- SQL Injection 은 EF Core 파라미터화로 방어되지만 비즈니스 규칙 우회 가능
- 약한 암호 정책 → 무차별 대입 공격에 취약
- GS 기능 적합성 항목 지적 사유

**조치 (PR #6/#7/#8)**
- FluentValidation 11.* 도입 + `AddValidatorsFromAssembly` 자동 등록
- 재사용 `Middleware/ValidationEndpointFilter.cs` — endpoint 1 줄로 적용
- **28 개 Validator 작성** (3 Tier 분리)

| Tier | PR | Validator 수 | 영역 |
|------|----|--------------|------|
| 1 | #6 | 4 | Auth (Login/Register), Kiosk Login, Inspection Result Upload |
| 2 | #7 | 6 | WorkOrder + Status + Lot, Admin Approval + ResetPassword, Alarm Action |
| 3 | #8 | 18 | Master 데이터 (Client/Recipe/Operator/Product/Maintenance/DefectCode/Shift/RecipeParameter), Operations (Heartbeat/Register/Disconnect/KioskLogout/Sensor/PerformMaintenance), Analytics (Reliability/Oee/ShiftReport/Report/Spc) |

- **보안 정책 강화**:
  - 회원가입 암호 `MinLength=4` → **8** (RegisterRequest)
  - 키오스크 PIN — **4-8 자리 숫자 정규식**
  - Validator 실패 시 RFC 7807 `ValidationProblemDetails` JSON 400 응답
- **단건 본문에만 적용**: 컬렉션 본문 (예: `List<RecipeParameterDto>` POST /batch) 은 v1.1 후속 처리 (§5.3)
- **`private record` 본문 미적용**: SettingsEndpoints 의 일부 record 는 외부 어셈블리 (Validators) 에서 참조 불가 — v1.1 후속 처리 (§5.2)

**검증**
- ✅ 빌드 통과 (오류 0, 경고 0)
- ✅ 통합 테스트로 정상/비정상 케이스 자동 확인 (#11)

---

## 4. High 4 항목 — 지적 가능성 처리 (v1.0)

### 4.1 헬스 체크 엔드포인트 부재
**취약점**
- `/health` endpoint 미구현 → Docker / Kubernetes / 모니터링 도구가 앱 상태 확인 불가
- DB 연결 끊김 등 장애 자동 감지 불능

**조치 (PR #9)**
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` NuGet 추가
- `AddDbContextCheck<BodaVmsDbContext>(name="sqlite-db")` — SQLite 연결 자동 검증
- `GET /health` — DB 포함 readiness probe (200 Healthy / 503 Unhealthy)
- `GET /health/live` — 프로세스 생존만 확인하는 liveness probe (200)
- 둘 다 `AllowAnonymous` — 토큰 없이 모니터링 도구 접근 가능

**검증**
- ✅ 스모크 테스트 `curl /health` → 200
- ✅ Integration 테스트 자동화 (#11)

---

### 4.2 OpenAPI / Swagger 문서 부재
**취약점**
- 23 endpoint 의 계약 문서 부재 → 외부 통합 / 운영팀 인계 시 가치 손실
- API 변경 추적 어려움

**조치 (PR #9)**
- `Swashbuckle.AspNetCore 6.*` NuGet 추가
- `AddEndpointsApiExplorer` + `AddSwaggerGen`
- 32 endpoint 의 계약 자동 노출
- **JWT Bearer 보안 스킴 등록** — Swagger UI 의 "Authorize" 버튼으로 토큰 입력 → 인터랙티브 테스트
- **운영 환경에도 활성** — 보호된 endpoint 는 JWT 필요해 데이터 노출 위험 없음, 운영 가치 큼

**검증**
- ✅ `/swagger` 접속 → 모든 endpoint 노출, JWT Authorize 동작 확인

---

### 4.3 익명 endpoint 접근 제어 (X-API-Key feature flag)
**취약점**
- 5 개 머신 **POST** endpoint (heartbeat / register / disconnect / inspection result / sensor reading) 가 익명 접근 가능
- **6 개 머신 GET endpoint** (parameters sync x2, lots x2, workorders/by-client, predictions/current) 도 동일 익명 — v1.0 에서 POST 만 처리, GET 은 v1.1 후속 (§5.1)
- VMS 데스크탑 클라이언트가 호출하는 endpoint 라 인증 도입 시 클라이언트 동시 업데이트 필요 — 호환성 위험 큼

**조치 (PR #10 + VMS PR #119, feature flag 패턴)**
- `Middleware/ClientApiKeyOptions.cs` + `ClientApiKeyEndpointFilter.cs` 신규
- `X-API-Key` 헤더 검증 — **상수 시간 비교 (timing attack 방지)**
- **`ClientApiKey:Required=false` 기본값** = 호환 모드 (헤더 있으면 검증, 없으면 통과)
- 운영자가 VMS 클라이언트 업데이트 완료 후 `Required=true` 전환 → enforcement 활성화
- VMS 데스크탑 측 (별도 repo, PR #119): `HeartbeatService` / `ParameterSyncService` / `SensorPollingService` 생성자에 `clientApiKey` 파라미터 추가, `HttpClient.DefaultRequestHeaders` 자동 적용. AppSetup wizard Page 2 에 "Web Client API Key" 입력 UI 신규.

**채택 사유**
- mTLS: PKI 인프라(cert 발급/배포/회전) 필요 → ops 비용 큼, 산업 내부망에는 과
- 클라이언트 JWT: 머신 자격 흐름 별도 설계 필요
- **X-API-Key**: 내부망 + TLS 환경에 적절한 보안 수준 + 구현 단순

**검증**
- ✅ Required=true + 헤더 없음 → 401
- ✅ Required=true + 잘못된 키 → 401
- ✅ Required=true + 정확한 키 → filter 통과
- ✅ Required=false + 헤더 없음 → 통과 (호환 모드)

---

### 4.4 단위 테스트 프로젝트 부재
**취약점**
- `BODA.VMS.Web.Tests` 프로젝트 존재 안 함 → 회귀 검출 자동화 불가
- 핵심 Validator / Service 로직 변경 시 수동 검증 의존

**조치 (PR #11)**
- `BODA.VMS.Web.Tests` xUnit 프로젝트 신규 (net8.0)
- NuGet: xUnit 2.9, FluentAssertions, FluentValidation.TestHelper, `Microsoft.AspNetCore.Mvc.Testing`
- **Validator 단위 테스트 9 클래스** (패턴별 대표)
- **Integration smoke test** — `WebApplicationFactory<Program>` + in-memory Jwt:Key 주입, `/health` 200 검증
- **`Program.cs` 에 `public partial class Program {}` 추가** — top-level statements 의 `internal Program` 가시성 확장

**버그 발견 — 즉각적 가치 입증**
테스트 작성 중 `AlarmActionRequestValidator` 의 체인 `.When()` 버그 발견:
- 두번째 `.When(x => !IsNullOrEmpty(Resolution))` 가 첫 `NotEmpty` 까지 영향 → Resolve+Resolution=null 에서 NotEmpty 가 절대 발화 안 함
- 수정: `NotEmpty` 와 `MaximumLength` 를 별도 `RuleFor` 로 분리

**검증**
- ✅ 9 → 69 → … → 384 테스트 누적 통과
- ✅ 실제 운영 버그 1 건 발견·수정
- v1.0 후속으로 19 Validator + 11 Service 통합 + SignalR Hub + DB 쓰기 endpoint + JWT 양성/Role 등 누적 확장 (§5 와 PR 인덱스 참조)

---

## 5. 잔여 후속 7 항목 처리 (v1.1, 2026-06-04)

v1.0 baseline 적용 후 GS 인증 신청은 가능했으나 다음 7 항목이 미해소 상태였음. v1.1 에서 모두 처리 완료.

### 5.1 익명 GET endpoint X-API-Key 비대칭
**취약점**
- v1.0 §4.3 는 **POST 5 개** 에만 `ClientApiKeyEndpointFilter` 적용
- VMS 머신이 호출하는 **GET 6 개** (parameters/sync x2, lots/by-number, lots/active-by-workorder, workorders/by-client, predictions/current) 는 미보호
- enforcement 모드 (`Required=true`) 에서 POST 는 막히는데 GET 은 열린 비대칭

**영향**
- GS 평가에서 "비인증 endpoint 가 운영 데이터 노출 여부" 항목 감점
- 내부망 침입 시 GET 으로 작업 지시/레시피 파라미터/예측 결과 등 조회 가능

**조치 (PR #30)**
- 6 개 GET endpoint 에 `.AddEndpointFilter<ClientApiKeyEndpointFilter>()` 부착
- 호환 모드(`Required=false`) 에서는 헤더 없어도 통과 — 기존 VMS 클라이언트 호환 유지

**검증 (자동 테스트 18 케이스)**
- ✅ EnforceMode + 헤더 없음 → 401 (6 endpoint Theory)
- ✅ EnforceMode + 잘못된 헤더 → 401 (3 대표 endpoint Theory)
- ✅ EnforceMode + 정확한 헤더 → filter 통과 (3 대표)
- ✅ CompatMode + 헤더 없음 → 통과 (6 endpoint Theory)

---

### 5.2 SettingsEndpoints `private record` 검증 불가
**취약점**
- `/api/settings/visionserver` PUT 의 본문 타입 `VisionServerSettingsRequest` 가 `private record` 로 선언
- 외부 어셈블리 (Validators, Tests) 에서 참조 불가 → FluentValidation 등록 자체 불가능
- 결과: BaseUrl 값이 무엇이든 통과해 `appsettings.json` 에 영구 기록
- `file:///C:/secret.txt`, `javascript:alert(1)`, `ftp://` 등도 그대로 저장

**영향**
- 운영 복구에 수동 편집 필요
- LFI 우회 (file://), 저장 후 클라이언트 렌더링 시 XSS 우회 (javascript:) 가능성

**조치 (PR #29)**
- `private record` → **public record** 로 노출 (`VisionServerSettingsRequest` / `VisionServerSettingsResponse`)
- 신규 `Validators/Admin/VisionServerSettingsRequestValidator.cs`:
  - `BaseUrl` null/빈값 허용 (Enabled=false 시나리오)
  - 값이 있으면 절대 http(s) URL + MaxLength(500)
  - `file://`, `javascript:`, `ftp://` 등 비-http 스키마 차단 (Uri.UriSchemeHttp/Https 화이트리스트)
- 엔드포인트에 `.AddEndpointFilter<ValidationEndpointFilter<VisionServerSettingsRequest>>()` 부착

**검증 (자동 테스트 18 케이스)**
- ✅ Validator 단위 11: 유효 URL 3 / null·empty 2 / 무효 6 (스키마 없음/상대경로/ftp/file/javascript:/형식) / 길이 초과 1
- ✅ 통합 7: 401 / User 403 / 무효 BaseUrl 4-Theory → 400 + `errors.BaseUrl`
- 양성 (성공 → appsettings.json 쓰기) 시나리오는 disk 부작용이라 통합에서 제외 — Validator 단위가 입력 매트릭스 커버

---

### 5.3 컬렉션 본문 검증 부재
**취약점**
- `ValidationEndpointFilter<T>` 는 단건 본문 `T` 만 인식
- `POST /api/parameters/batch` 가 `List<RecipeParameterDto>` 받지만 검증 미적용
- 한 항목이 invalid 여도 그대로 통과 → DB 에 부정확한 파라미터 저장

**영향**
- GS 입력 검증 항목에서 컬렉션 endpoint 가 단건과 동일 표준을 따르지 않는다는 평가 가능

**조치 (PR #28)**
- 신규 `Middleware/CollectionValidationEndpointFilter<T>`:
  - `IEnumerable<T>` 인수의 각 항목을 `IValidator<T>` 로 검증
  - 에러 키를 `[index].Property` 형식으로 집계 → ValidationProblemDetails (RFC 7807)
  - validator 미등록 / null / 빈 컬렉션은 `next()` 통과 (현행 약속 보존)
- `/api/parameters/batch` 에 부착

**검증 (자동 테스트 5 케이스)**
- ✅ 토큰 없음 → 401
- ✅ 모두 valid → 201 + DB N 행
- ✅ 한 항목 invalid → 400 + `[1].Description` 인덱스 키 + DB 0 행 (전체 거부)
- ✅ 다중 invalid → 인덱스별 각 에러 집계
- ✅ 빈 리스트 → 201 + 0 insert (약속 보존)

---

### 5.4 Service 통합 테스트 커버리지 부족
**취약점**
- v1.0 §4.4 단위 테스트 프로젝트는 Validator 9 클래스 + integration smoke 만 커버
- 11 개 핵심 서비스의 비즈니스 로직 (상태 머신, 채번, 멱등성, 보안) 회귀 자동 검출 불능
- IATF/ISO 추적성 단위 (WorkOrder/Lot 상태, EquipmentStatus 시계열, Operator 인증) 결함이 통과해도 탐지 어려움

**영향**
- 코드 변경 시 회귀 자동 검출 부족
- GS 유지보수성 항목에서 "서비스 레이어 단위 테스트 커버리지" 평가 감점 위험

**조치 (PR #20~#27)**
- **11 개 Service 통합 테스트 추가** (in-memory SQLite + 비즈니스 규칙)

| PR | 서비스 | 케이스 | 핵심 |
|----|--------|--------|------|
| #20 | WorkOrderService | 19 | Planned→InProgress→Completed→Closed 상태 머신, OrderNo 일일 채번, Closed 감사 보호 |
| #21 | LotService | 11 | LotNumber `YYYYMMDD-OrderNo-Seq` 채번, Closed WorkOrder 차단 |
| #22 | MaintenanceService | 16 | SEMI E10 PM 일정/NextDueAt 진행, 추적성 보호 |
| #23 | EquipmentStatusService | 10 | 같은 상태 noop (row 폭발 방지), 시간 윈도우 4 매트릭스 |
| #24 | OperatorService | 22 | BCrypt PIN 해시 (평문 미보존), 비활성 작업자 차단, 세션 이력 보호 |
| #25 | OperatorSessionService | 14 | 라인별 활성 세션 1 개 불변, EndReason 분류 (Auto/ShiftChange) — `NoopHubContext<THub>` 헬퍼 신규 |
| #26 | AlarmService | 15 | ISA-18.2 New→Ack→Resolved, Ack 멱등성 (첫 확인자 보호), skip-ack 자동 채움 |
| #27 | HistoryService | 9 | 다중 필터, ToolResults JSON 안전, ClosedXML xlsx round-trip |

- 신규 헬퍼: `BODA.VMS.Web.Tests/Helpers/NoopHubContext.cs` — `IHubContext<THub>` 의존 서비스용

**검증**
- ✅ 누적 116 케이스 통과 (#20~#27 합계)
- ✅ 전체 테스트 217 → 333 (v1.0 217 → v1.1 §5 후속까지 384)

---

### 5.5 Content-Security-Policy 미적용
**취약점**
- v1.0 보안 헤더 4 종에 CSP 누락
- XSS / 데이터 유출 / 외부 폼 제출 / clickjacking 보강 표준 미달

**영향**
- GS 보안성 항목에서 OWASP top 10 (A03 Injection / A07 ID & Auth Failures) 완화 누락 평가

**조치 (PR #31)**
- `SecurityHeadersMiddleware.DefaultContentSecurityPolicy` 상수 신규
- Blazor WASM 호환 정책:

| 디렉티브 | 값 | 이유 |
|----------|-----|------|
| default-src | 'self' | 모든 리소스 동일 origin 기본 |
| script-src | 'self' 'wasm-unsafe-eval' | Blazor WASM `WebAssembly.instantiate` 필수 |
| style-src | 'self' 'unsafe-inline' | MudBlazor 인라인 스타일 (CSP 가이드상 style 인라인은 허용 가능) |
| img-src | 'self' data: blob: | 검사 이미지 + 로고 data URI |
| font-src | 'self' data: | 로컬 폰트 |
| connect-src | 'self' ws: wss: | SignalR WebSocket/LongPolling |
| object-src | 'none' | Flash/플러그인 차단 |
| base-uri | 'self' | `<base>` 태그 hijack 방어 |
| frame-ancestors | 'self' | X-Frame-Options 보강 (CSP3) |
| form-action | 'self' | 외부 폼 제출 차단 |

- **운영 override 경로**: `SecurityHeaders:ContentSecurityPolicy` 환경변수/appsettings 로 정책 교체 — CDN/외부 도메인 화이트리스트가 필요할 때 코드 변경 없이 적용

**검증 (자동 테스트 2 케이스)**
- ✅ CSP 헤더 존재 + 핵심 9 디렉티브 검증 (`'wasm-unsafe-eval'`, default-src 'self', object-src 'none', ws:/wss:, img-src data: blob: 등)
- ✅ `SecurityHeaders:ContentSecurityPolicy` override 동작 (IClassFixture 패턴)

---

### 5.6 구조화 로깅 / Observability 부재
**취약점**
- 기본 `Microsoft.Extensions.Logging` 은 사람 가독성 텍스트 포맷
- 사후 분석 / SIEM 인제스트 / 구조화 쿼리 불가
- 장애 추적시 grep 의존 — 분산 컨텍스트 (correlation, 요청 elapsed, status code) 누락

**영향**
- GS 신뢰성/유지보수성 항목에서 "운영 가관측성 (observability)" 평가 미달
- 운영 사고 발생 시 RCA 시간 증가

**조치 (PR #32)**
- NuGet: `Serilog.AspNetCore 8.*` / `Serilog.Sinks.File 6.*` / `Serilog.Formatting.Compact 3.*`
- `Program.cs`: `builder.Host.UseSerilog(...)`:
  - Sink: Console + 일일 롤링 JSON 파일 (`Logs/boda-vms-{Date}.json`)
  - Enrich: `Application=BODA.VMS.Web`, `Environment`, `FromLogContext`
  - 기본 레벨 Information / Microsoft.AspNetCore + EFCore = Warning
  - 14 일 자동 retention — 디스크 폭주 방지
- `app.UseSerilogRequestLogging()` — Method / Path / StatusCode / Elapsed 자동 캡처 (SecurityHeaders 이전)
- `SerilogObservability:Disabled` 환경변수로 파일 sink 토글 (테스트/CI 부작용 차단)

**검증 (자동 테스트 2 케이스)**
- ✅ `IDiagnosticContext` DI 등록 확인 → UseSerilog 활성 증명
- ✅ UseSerilogRequestLogging 부착 후 응답 회귀 없음

**운영 활용**
- 로그 파일 → SIEM (Splunk/ELK) 인제스트 또는 `jq` 로 사후 분석
- 구조화 키: `Application`, `Environment`, `RequestMethod`, `RequestPath`, `StatusCode`, `Elapsed`, `traceId`, …

---

### 5.7 DB 백업/복구 자동화 부재
**취약점**
- 운영 가이드에 수동 백업 절차만 기술 (Stop service → file copy → Start)
- 운영자가 잊으면 DB 손상 시 복구 불가
- 백업 무결성 검증 절차 부재

**영향**
- GS 신뢰성 항목 — 데이터 무결성 / 복구성 평가 미달
- 산업용 vision 시스템 특성상 검사 이력 손실은 추적성 (IATF 16949) 위반

**조치 (PR #33)**
- 신규 `Services/DatabaseBackupOptions.cs`:
  - Enabled (기본 true) / IntervalMinutes (기본 1440=24h) / BackupOnStartup (기본 false) / Destination (기본 `{DbDirectory}/backups`) / RetainCount (기본 14)
- 신규 `Services/DatabaseBackupService.cs` (`BackgroundService`):
  - `SqliteConnection.BackupDatabase` API (페이지 단위 복사) — **운영중 무중단**
  - `ConnectionStrings:DefaultConnection` 에서 `DataSource` 자동 추출
  - 파일명: `boda-vision-{yyyyMMdd-HHmmss}.db`
  - 매 백업 후 RetainCount 초과 파일을 `LastWriteTime` 오름차순 삭제
  - 백업 실패는 `LogError` 만 — 다음 주기 자동 재시도 (앱 중단 금지)
- `Program.cs`: `AddHostedService<DatabaseBackupService>` + Options 바인딩
- `IntegrationTestFactory`: `DatabaseBackup:Enabled=false` 주입 — 테스트 bin/backups 부작용 차단

**검증 (자동 테스트 6 케이스)**
- ✅ PerformBackup → 행 수 정확히 복제 (페이지 단위 검증, 42 rows)
- ✅ 소스 누락 → FileNotFoundException
- ✅ PruneOldBackups: N 만 남기고 삭제, `boda-vision-*.db` 패턴 외 파일(README 등) 보존
- ✅ retainCount 0/음수 → no-op
- ✅ 디렉토리 없어도 안전 (첫 가동 시나리오)
- ✅ 연속 2 회 백업 → 파일명 분리 (타임스탬프 충돌 없음)

**운영 운용**
- 운영에서 `DatabaseBackup:Destination` 을 별도 디스크/네트워크 드라이브 경로로 지정 권장 (DB 파일과 동일 디스크 손상시 백업도 함께 손실되는 단일 실패점 회피)

---

## 6. After — 갱신된 평가표 (v1.2)

| 품질 특성 | Before | After v1.0 | After v1.1 | After v1.2 | 비고 |
|-----------|--------|------------|------------|------------|------|
| 기능 적합성 | 중 | 상 | 상+ | **상+** | + KISA 패스워드 복잡도 정책 |
| 신뢰성 | 낮음 | 상 | 상+ | **상+** | + 인증 감사 로그 (성공/실패/잠금) |
| **보안성** | 매우낮음 | 상 | 상+ | **최상** | + brute-force 다층 방어 + 열거 방지 + CSP 인라인 제거 |
| 성능 효율성 | 중상 | 중상 | 중상 | 중상 | 무변경 (별도 영역) |
| 호환성 | 중 | 상 | 상 | 상 | Swagger 그대로 |
| 사용성 | 중상 | 중상 | 중상 | **중상+** | + 폰트 self-host (폐쇄망 오프라인 운영) |
| 유지보수성 | 중 | 상 | 상+ | **상+** | + ClientService/ProductService + 인증 강화 (총 443 테스트) |
| 이식성 | 중상 | 중상 | 중상 | 중상 | DEPLOYMENT_GUIDE 보강 그대로 |

---

## 7. 적용 PR 인덱스 (시간순)

### v1.0 — Critical + High baseline (2026-06-02)

| # | 제목 | 영향 |
|---|------|------|
| #5 | Critical baseline — JWT 외부화 + 글로벌 예외 + 보안 헤더 + CORS | Critical 3/4 |
| #6 | Tier 1 Validator (Auth + Kiosk + Inspection 4 개) | Critical 4/4 시작 |
| #7 | Tier 2 Validator (WorkOrder + Admin + Alarm 6 개) | Critical 4/4 진행 |
| #8 | Tier 3 Validator (Master + Operations + Analytics 18 개) | Critical 4/4 완료 |
| #9 | 헬스 체크 + Swagger | High 2/4 |
| #10 | X-API-Key feature flag (Web 측 POST 5 개) | High 3/4 |
| #11 | 단위 테스트 프로젝트 + 9 Validator + Integration smoke | High 4/4 완료 |
| **VMS #119** | X-API-Key 헤더 송신 (VMS 데스크탑 측, 짝 작업) | Web #10 의 운영 전환 가능 |

### v1.0 후속 점진 확장 (테스트 누적)

| # | 제목 | 누적 테스트 |
|---|------|-------------|
| #12~#16 | Validator 잔여 19 개 단위 테스트 / Service 3 통합 / 미들웨어 4 영역 / DB 쓰기 endpoint / JWT 양성/Role | 9 → 214 |
| #17~#18 | SignalR Hub 인증 (negotiate 401 + ?access_token=) | 214 → 217 |
| #19 | **VmsHub `[Authorize]` 추가 — #18 발견 갭 해소** | 217 |

### v1.1 — 잔여 7 항목 후속 처리 (2026-06-04)

| # | 항목 | 누적 테스트 |
|---|------|-------------|
| #20 | Service 통합 — WorkOrderService 19 케이스 | 236 |
| #21 | Service 통합 — LotService 11 케이스 | 247 |
| #22 | Service 통합 — MaintenanceService 16 케이스 | 263 |
| #23 | Service 통합 — EquipmentStatusService 10 케이스 | 273 |
| #24 | Service 통합 — OperatorService 22 케이스 (BCrypt 보안) | 295 |
| #25 | Service 통합 — OperatorSessionService 14 + `NoopHubContext` 헬퍼 | 309 |
| #26 | Service 통합 — AlarmService 15 (ISA-18.2 멱등성) | 324 |
| #27 | Service 통합 — HistoryService 9 (xlsx round-trip) | 333 |
| **#28** | **잔여 #4** — CollectionValidationEndpointFilter + `/batch` 적용 | 338 |
| **#29** | **잔여 #3** — VisionServerSettingsRequest public 화 + URL 검증 | 356 |
| **#30** | **잔여 #2** — 익명 GET 6 endpoint X-API-Key 부착 | 374 |
| **#31** | **잔여 #5** — Content-Security-Policy (Blazor WASM 호환) | 376 |
| **#32** | **잔여 #6** — Serilog 구조화 로깅 + 일일 롤링 JSON | 378 |
| **#33** | **잔여 #7** — SQLite 자동 온라인 백업 + retention | **384** |

### v1.1 후속 — Service 통합 테스트 확대 (§8 item 3 처리, 2026-06)

| # | 제목 | 누적 테스트 |
|---|------|-------------|
| #35 | Web admin 시드 환경변수 기반 + 미설정 시 부팅 차단 (Option C2) | 384 |
| #36 | ClientService + ProductService 통합 테스트 25 건 (HttpMessageHandler stub) | 409 |
| #37 | ClientService VisionServer 활성 분기 12 테스트 | 421 |

### v1.2 — 인증 강화 (auth hardening, 2026-06-10)

| 항목 | 내용 | 누적 테스트 |
|------|------|-------------|
| §10.1 | brute-force 다층 방어 — IP rate limit(429) + 계정 잠금 | 430 |
| §10.2 | 계정 열거(enumeration) 방지 — 균일 401 | 430 |
| §10.3 | 인증 감사 로그 — LoginSuccess/Failed/Lockout + 키오스크 PIN | 431 |
| §10.4 | KISA 패스워드 복잡도 (회원가입 + 재설정) | 440 |
| §10.5 | Kestrel 요청 본문 크기 상한 명시 (DoS) | 440 |
| §10.6 | CSP 강화 — 인라인 스크립트 제거 + 폰트 self-host(오프라인) | **443** |

---

## 8. 후속 발전 영역 (인증 영향 없음, 점진 고려)

v1.1 시점 GS 인증 신청에 추가 작업 불필요. 운영 안정성/관측성을 더 강화하고 싶다면:

1. **OpenTelemetry / Application Insights** — Serilog 파일 도입(§5.6), 분산 트레이싱은 미도입. 마이크로서비스 확장 시 고려.
2. **백업 복구 자동 검증** — 현재는 생성만(§5.7), 복구 리허설은 수동. 정기 restore-test job 추가 가능.
3. ~~**Service 통합 테스트 확대** — ClientService / ProductService~~ → **✅ 처리 완료** (PR #36/#37, HttpMessageHandler stub 으로 IHttpClientFactory 의존 격리, 37 케이스).
4. **모바일 키오스크 endpoint 인증 강화** — `/api/kiosk/login` 은 v1.2 §10.1 로 IP rate limit + PIN 실패 감사 적용. 나머지 `/api/kiosk` 그룹은 익명 유지 (UX/operator 흐름 영향 분석 필요).
5. **CI/CD 파이프라인** — GitHub Actions / Azure Pipelines. 현재는 로컬 빌드/테스트 + 수동 배포.

---

## 9. 운영 전환 절차

### 9.1 X-API-Key Enforcement 활성화

본 baseline 적용 직후 X-API-Key 는 **호환 모드(Required=false)** 로 동작 — 기존 VMS 클라이언트 무영향. enforcement 활성화 절차:

1. **Web 서버 환경변수 설정** (관리자 PowerShell):
   ```powershell
   [Environment]::SetEnvironmentVariable("Jwt__Key", "<32자 이상 키>", "Machine")
   [Environment]::SetEnvironmentVariable("ClientApiKey__Value", "<무작위 비밀>", "Machine")
   ```

2. **VMS 클라이언트 PC 각각 업데이트**:
   - `AppSetup.exe` 재실행 → Page 2 "Web Server Integration"
   - "Web Client API Key" 필드에 동일 비밀 입력 → 저장
   - `%LocalAppData%\BODA VISION AI\system_config.json` 에 `clientApiKey` 필드 생성 확인

3. **VMS 클라이언트 재시작** → heartbeat 요청에 `X-API-Key` 헤더 송신 확인 (Fiddler / Wireshark)

4. **Enforcement 활성화** (Web 서버):
   ```powershell
   [Environment]::SetEnvironmentVariable("ClientApiKey__Required", "true", "Machine")
   ```
   → Web 서비스 재시작

5. **검증** — VMS 가 정상 등록·heartbeat 유지되는지 모니터링. 잘못된 키 또는 헤더 누락 클라이언트는 401 + 자동 재연결 시도. 본 enforcement 는 **POST 5 + GET 6** 모두 적용 (v1.1 §5.1).

### 9.2 Serilog 로그 운용 (v1.1 §5.6)

- **파일 경로**: `{ContentRoot}/Logs/boda-vms-{Date}.json` (UTF-8 JSON-per-line)
- **포맷**: Serilog CompactJsonFormatter — `{"@t":"timestamp","@l":"Information","RequestPath":"/api/...",...}`
- **Retention**: 14 일 자동 (RollingInterval.Day, retainedFileCountLimit=14)
- **SIEM 연동**: Splunk Universal Forwarder / Elastic Beats 로 인제스트, 또는 `jq` 로 즉시 분석:
   ```bash
   jq 'select(.StatusCode >= 500)' Logs/boda-vms-20260604.json
   ```
- **테스트/CI 환경**: `SerilogObservability:Disabled=true` 환경변수로 파일 sink 비활성

### 9.3 DB 자동 백업 운용 (v1.1 §5.7)

- **백업 경로**: 기본 `{DbDirectory}/backups/boda-vision-{yyyyMMdd-HHmmss}.db`
- **주기**: 기본 24h (`DatabaseBackup:IntervalMinutes=1440`)
- **Retention**: 14 파일 (`DatabaseBackup:RetainCount`)
- **운영 권장**: `DatabaseBackup:Destination` 을 **별도 디스크/네트워크 드라이브** 로 지정
  ```powershell
  [Environment]::SetEnvironmentVariable("DatabaseBackup__Destination", "\\backup-server\boda-vms\db", "Machine")
  ```
  → DB 파일과 동일 디스크 손상 시 백업도 함께 손실되는 단일 실패점 회피
- **복구 절차**:
   1. BODA.VMS.Web 서비스 중지
   2. 손상된 `BodaVision.db` 를 백업 폴더의 최신 `boda-vision-*.db` 로 교체 (파일명 `BodaVision.db` 로 변경)
   3. 서비스 재시작 → `/health` 200 확인

### 9.4 CSP 정책 운용 (v1.1 §5.5)

- 기본은 `SecurityHeadersMiddleware.DefaultContentSecurityPolicy` 상수 (§5.5 표 참조)
- 운영에서 CDN/외부 API 도메인 화이트리스트 필요 시 환경변수로 override:
   ```powershell
   [Environment]::SetEnvironmentVariable(
     "SecurityHeaders__ContentSecurityPolicy",
     "default-src 'self' https://cdn.example.com; script-src 'self' 'wasm-unsafe-eval'",
     "Machine")
   ```
- 적용 후 브라우저 DevTools Console 에서 CSP 위반 경고 모니터링 권장

### 9.5 인증 강화 옵션 운용 (v1.2 §10)

- **로그인 rate limit** — 기본 IP 단위 60초 5회. 운영 환경/공유 NAT 사정에 맞춰 조정:
   ```powershell
   [Environment]::SetEnvironmentVariable("LoginRateLimit__PermitLimit", "5", "Machine")
   [Environment]::SetEnvironmentVariable("LoginRateLimit__WindowSeconds", "60", "Machine")
   ```
   - reverse proxy 뒤에 배치 시 `X-Forwarded-For` 가 실제 IP 로 해석되도록 `ForwardedHeaders` 미들웨어 구성 필요 (그렇지 않으면 모든 클라이언트가 프록시 IP 단일 파티션 공유)
- **계정 잠금** — 기본 연속 5회 실패 → 15분 잠금. `MaxFailedAttempts=0` 으로 비활성 가능:
   ```powershell
   [Environment]::SetEnvironmentVariable("AccountLockout__MaxFailedAttempts", "5", "Machine")
   [Environment]::SetEnvironmentVariable("AccountLockout__LockoutMinutes", "15", "Machine")
   ```
   - 키오스크 Operator 는 계정 잠금 미적용(라인 가용성 우선) — PIN brute-force 는 rate limit 으로 방어
- **인증 감사 조회** — 의심 활동 추적:
   ```sql
   SELECT Timestamp, UserName, IpAddress, Action, Changes
   FROM AuditLogs WHERE EntityName IN ('Auth','KioskAuth')
   ORDER BY Timestamp DESC;
   ```
- **패스워드 정책** — KISA 복잡도(3종 8자+ / 2종 10자+)는 코드 상수(`PasswordPolicy`). 기존 계정 암호는 소급 적용 안 됨 — 차기 변경/재설정 시 적용

---

## 10. v1.2 — 인증 강화 (auth hardening, 2026-06-10)

v1.1 baseline 적용 후 인증(authentication) 경로를 OWASP A07(Identification & Authentication Failures) 기준으로 추가 점검. brute-force 다층 방어, 패스워드 복잡도, 인증 감사, CSP 잔여 강화 등 **6 항목** 처리. `feat/gs-auth-hardening` 브랜치.

### 10.1 로그인 brute-force 방어 부재 (rate limiting + 계정 잠금)
**취약점**
- `/api/auth/login` / `/api/kiosk/login` 에 시도 횟수 제한 없음 → 무제한 무차별 대입 가능
- 특히 키오스크 PIN(4-8 자리 숫자)은 조합 공간이 작아(최대 10^8) 자동화 공격에 취약
- 계정 단위 잠금도 없어 단일 계정 표적 공격 무방비

**영향**
- GS 보안성 항목 — 자격증명 무차별 대입 방어 부재 지적
- 약한 PIN 정책과 결합 시 키오스크 계정 탈취 현실적 위험

**조치**
- **1차 (IP 단위 rate limit)**: `Middleware/LoginRateLimitOptions.cs` + `Program.cs` `AddRateLimiter` — IP partition fixed-window (기본 60초 5회 초과 시 **429**). `.RequireRateLimiting("login")` 을 두 로그인 endpoint 에만 부착. 거부 응답은 RFC 7807 `application/problem+json` (`ApiExceptionHandler` 포맷 일관)
- **2차 (계정 단위 잠금)**: `Services/AccountLockoutOptions.cs` + `AuthService` — 연속 실패 `MaxFailedAttempts`(기본 5) 도달 시 `LockoutMinutes`(기본 15) 동안 잠금. 잠금 중에는 정확한 암호도 차단. 성공 시 카운트/잠금 리셋. `MaxFailedAttempts<=0` 이면 비활성
- `User` 엔티티에 `FailedLoginCount` / `LockoutUntil` 컬럼 추가 — `Program.cs` 의 `PRAGMA table_info` 기반 **idempotent 마이그레이션** (기존 DB 무중단 ALTER)
- **키오스크 Operator 는 계정 잠금 미적용** — 현장 라인 가용성 우선(잠금으로 생산 중단 방지), PIN brute-force 는 IP rate limit 으로 방어. 설계 의도를 옵션 XML 주석에 명시
- 옵션은 요청 시점 `IOptions` 해석 — `WebApplicationFactory` 의 테스트 설정 주입 타이밍(Build 시점) 호환

**검증 (자동 테스트 9 케이스)**
- ✅ AuthServiceLockoutTests 6: 연속 실패 → 잠금, 잠금 중 정확 암호 차단, 성공 시 리셋, 만료 후 재허용, 미승인 계정 카운트 미증가, `MaxFailedAttempts=0` 비활성
- ✅ LoginRateLimitTests 3: auth/login 한도 초과 429 + ProblemDetails, kiosk/login 429, 정책 미부착 endpoint(`/health/live`) 무제한

### 10.2 계정 열거(username enumeration) 가능
**취약점**
- 기존 `LoginAsync` 는 "존재하지 않는 사용자" 와 "암호 불일치" 를 코드 경로상 구분 — 응답/타이밍 차이로 유효 사용자명 추측 가능

**영향**
- 공격자가 유효 계정 목록을 먼저 수집 → 표적 무차별 대입 효율 상승 (OWASP A07)

**조치**
- 모든 실패 경로(미존재/잠금/암호불일치/미승인)가 **동일하게 null → 401** 반환
- 사유 구분은 감사 로그(`Changes`)에만 기록 — 클라이언트 응답에서는 불가관측

**검증**
- ✅ AuthServiceTests / AuthEndpointTests 의 음성 케이스가 모두 동일 401 확인

### 10.3 인증 이벤트 감사 로그 부재
**취약점**
- 로그인 성공/실패/잠금 이력이 남지 않음 → brute-force 흔적·계정 탈취 사후 추적 불가
- `AuditInterceptor` 는 엔티티 CRUD 만 기록, 인증 이벤트는 대상 외

**영향**
- GS 보안성/신뢰성 — 보안 사고 RCA(근본원인분석) 및 추적성 미달
- IATF 16949 추적성 관점에서도 접근 이력 부재

**조치**
- `AuditLog.Action` 에 `LoginSuccess` / `LoginFailed` / `Lockout` 상수 추가 (`MaxLength` 10 → 20)
- `AuthService.AuditAuthAsync` — 웹 로그인 이벤트를 `EntityName="Auth"` 로 기록 (UserId/UserName/IpAddress/사유). User 의 실패 카운트 변경과 **동일 SaveChanges** 로 원자적 영속화
- 키오스크 PIN 실패는 `OperatorEndpoints` 에서 `EntityName="KioskAuth"` 로 기록 (성공은 OperatorSessions 행 자체가 이력)
- `AuditInterceptor` ignore 목록에 `FailedLoginCount`/`LockoutUntil` 추가 — AuthService 가 전용 인증 감사를 남기므로 일반 Update 감사 중복 방지

**검증**
- ✅ AuthServiceLockoutTests — LoginFailed/Lockout/LoginSuccess 카운트, 미존재 사용자는 UserId=null + UserName/IP 기록 확인
- ✅ AuthEndpointTests — full HTTP path 로 실패 시 `EntityName="Auth"` 감사 1 건 검증

### 10.4 패스워드 복잡도 정책 미흡 (KISA)
**취약점**
- v1.0 에서 최소 길이만 8 자로 강화했으나 문자 종류(복잡도) 요건 없음 → `abcdefgh`, `12345678` 등 약한 패턴 통과

**영향**
- GS 기능 적합성/보안성 — 약한 자격증명이 무차별 대입/사전 공격에 취약

**조치**
- `Validators/PasswordPolicy.cs` — KISA 패스워드 가이드라인: **3종 조합(대/소/숫/특 중) 8자+ 또는 2종 조합 10자+**. `MustSatisfyPasswordComplexity()` 재사용 확장
- `RegisterRequestValidator` + `ResetPasswordRequestValidator` 양쪽에 동일 정책 적용

**검증 (자동 테스트)**
- ✅ RegisterRequestValidatorTests Theory: 통과 3종(4종 8자/2종 10자/3종 8자) + 차단 4종(2종 8자/1종 다수, 과거 약패턴 `secret12` 포함)
- ✅ ResetPasswordRequestValidatorTests Theory: 9자 2종·11자 1종 차단

### 10.5 요청 본문 크기 상한 부재 (DoS)
**취약점**
- Kestrel 기본 `MaxRequestBodySize`(30MB) 가 명시되지 않아 운영 의도 불명확 — 과대 본문 업로드로 메모리 압박 가능

**조치**
- `appsettings.json` 의 `Kestrel:Limits:MaxRequestBodySize` 명시(30MB — 검사 이미지 JPEG 여유 포함). 기본 호스트가 `Kestrel` 섹션을 자동 바인딩하므로 코드 변경 불필요, 운영 override 가능

**검증**
- ✅ 빌드/기존 업로드 경로(검사 이미지) 회귀 없음 확인

### 10.6 CSP 잔여 강화 — 인라인 스크립트 제거 + 폰트 self-host
**취약점**
- §5.5 CSP 도입에도 `App.razor` 에 boot-loader 인라인 `<script>` 잔존 → `script-src` 가 인라인 허용에 의존
- Google Fonts CDN(`fonts.googleapis.com`) 외부 의존 — 폐쇄망(오프라인) 운영 불가 + CSP `connect/style-src` 외부 origin 필요

**영향**
- 인라인 스크립트는 XSS 주입 표면 확대, 엄격 CSP 적용 장애물
- 외부 CDN 의존은 산업 폐쇄망 GS 운영 환경(인터넷 차단)에서 폰트 로드 실패 → 사용성 저하

**조치**
- boot-loader 인라인 스크립트를 `wwwroot/js/boot-loader.js` 로 외부화 → `script-src 'self'` 만으로 충족
- Montserrat/Roboto/Roboto Mono 를 `wwwroot/fonts/*.woff2` variable font 로 self-host, `app.css` `@font-face` 로 전환 — 외부 CDN 의존 완전 제거(오프라인 운영)
- `app.css?v=` 캐시 버스트 갱신, `.gitignore` 에 Serilog `Logs/` 추가(운영 로그 커밋 차단)

**검증**
- ✅ 빌드 통과, 폰트/부트로더 정상 로드, 외부 네트워크 요청 제거 확인

---

## 11. 참고 자료

- ISO/IEC 25010:2011 — Systems and software Quality Requirements and Evaluation (SQuaRE) — System and software quality models
- ISO/IEC 25023:2016 — Measurement of system and software product quality
- TTA GS 인증 평가 기준 (한국정보통신기술협회)
- ASP.NET Core 8 보안 가이드 — https://learn.microsoft.com/aspnet/core/security/
- OWASP Top 10 2021 — https://owasp.org/Top10/
- FluentValidation Documentation — https://docs.fluentvalidation.net/
- Serilog Documentation — https://serilog.net/
- ISA-18.2 — Management of Alarm Systems for the Process Industries (AlarmService §5.4 #26 의 기준)
- IATF 16949 — Quality Management System (검사 이력 추적성 §5.4 의 동기)
- SEMI E10 — Specification for Definition and Measurement of Equipment Reliability, Availability, and Maintainability (RAM) (MaintenanceService/EquipmentStatusService §5.4 의 기준)

---

**문서 끝.**
