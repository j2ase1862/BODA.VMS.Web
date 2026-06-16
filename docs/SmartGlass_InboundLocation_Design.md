# 스마트 글라스(Moziware Cimo) 입고 위치 조회 — BODA.VMS.Web(Blazor) 도입 설계

> 원안(`스마트글라스_입고위치조회_설계.md`)을 **이 저장소의 실제 스택(Blazor WebAssembly)** 에 맞게 재작성한 버전.
> 원안은 React + Three.js 프런트와 창고(WMS) 데이터 모델을 전제했으나, BODA.VMS.Web에는 **React/Three.js도, 창고 데이터도 없다.** 본 문서는 그 간극을 반영한다.

---

## 0. 원안 대비 무엇이 달라졌나 (먼저 읽을 것)

| 항목 | 원안 전제 | BODA.VMS.Web 실제 | 본 설계의 처리 |
|------|-----------|-------------------|----------------|
| 프런트엔드 | React + Three.js 재활용 | **Blazor WebAssembly (C#/Razor)**. npm/React/Three.js 전무 | Razor 컴포넌트로 신규 작성. "버튼=음성" 아이디어는 그대로 유효(§4) |
| 3D 자산 | 기존 디지털 트윈/AOI/AMR 재활용 | 재활용할 3D 자산 **없음** | 2차 목표는 Three.js를 **JS interop로 신규 도입**(§9) |
| 입고 위치 데이터 | `_db.Items`(Barcode/Zone/Rack/Bin/좌표) 존재 가정 | **창고/재고 엔티티 없음**. 도메인은 비전검사·MES | **신규 엔티티 + SQLite 부트스트랩**(§8). *데이터 출처 정의가 선결 과제* |
| API 스타일 | MVC `[HttpGet]` 컨트롤러 | **Minimal API** 엔드포인트 그룹(`Endpoints/*.cs`) | `MapGlassEndpoints()` 패턴(§7) |
| 인증 | 언급 없음 | JWT `[Authorize]` 강제 + 머신/키오스크용 `X-API-Key` 필터 | 핸즈프리 → **키오스크 선례(`/api/kiosk`) 차용**(§6) |
| 바코드 | `import @zxing/browser` (ES 모듈) | Blazor엔 JS 번들러 없음 | **정적 스크립트 + `IJSRuntime` interop**(§5) |

**핵심 메시지:** 도입은 가능하지만 "엔드포인트 하나만 추가"가 아니라 **(a) 창고 데이터 모델 + (b) Blazor glass 페이지 + (c) 바코드 JS interop** 의 신규 구축이다. 가장 먼저 정할 것은 기술이 아니라 **"입고 위치 데이터가 어디서 오는가"**.

---

## 1. 목표 (원안 유지)

작업자가 스마트 글라스를 착용한 채:

1. 물류 바코드를 스캔하고
2. **"입고"** / **"입고제품"** 음성 명령을 내리면
3. BODA.VMS.Web이 DB에서 입고 위치를 조회해 글라스로 전송
4. 글라스 화면에 위치 표시
   - **1차**: 위치 텍스트
   - **2차**: 공장 도면에 위치 하이라이트(3D 뷰어)

---

## 2. 기기 전제 (원안 유지)

- Moziware Cimo = RealWear 기반, **Android 10 + Infinity OS**
- 단안 보조 디스플레이 **854×480 / FOV 20°** (몰입형 VR/AR 불가)
- 음성 중심 UX, 내장 바코드 스캐너 / 16MP 카메라 / 레이저 포인터
- **별도 네이티브 APK 불필요** — 기기 브라우저의 웹 페이지로 구현 (= Path A)

원안의 경로 비교(A 웹기반 / B MAUI / C Kotlin / D Unity)에서 **A 채택**은 동일하다. 다만 "A = React 재활용"이 아니라 **"A = 기존 Blazor 호스트에 `/glass` 라우트 추가"** 로 재정의한다.

---

## 3. 아키텍처 — 기존 Blazor Web App에 얹기

```
BODA.VMS.Web (ASP.NET Core 8 호스트)
├─ Server
│   ├─ Endpoints/GlassEndpoints.cs        ← 신규 Minimal API ( /api/glass )
│   ├─ Services/IWarehouseService.cs       ← 신규 조회 서비스
│   ├─ Data/Entities/WarehouseItem.cs      ← 신규 엔티티 (입고 위치 마스터)
│   └─ Program.cs                          ← 테이블 부트스트랩 + MapGlassEndpoints()
│
└─ Client (Blazor WASM)
    ├─ Pages/Glass.razor                   ← 신규 /glass 라우트 (EmptyLayout 풀스크린)
    ├─ Models/GlassDto.cs                  ← 신규 DTO (AndonDto 와 같은 위치)
    └─ wwwroot/js/glassScanner.js          ← 신규 @zxing 래퍼 (JS interop)
```

기존 Andon 보드(`Pages/Andon.razor` + `EmptyLayout` + `/api/andon`)가 **그대로 좋은 본보기**다. 풀스크린 전용 화면, 폴링/실시간 갱신, 단일 엔드포인트 조회 패턴을 동일하게 따른다.

> **DTO 위치:** 이 저장소는 web↔WASM 전용 DTO를 `BODA.VMS.Web.Client/Models/`에 둔다(예: `AndonDto.cs`). 본 설계도 동일하게 둔다. *솔루션 간(.NET Framework Vision Engine 포함) 공유가 필요해지면* CLAUDE.md 규칙대로 `BODA.VMS.ShareLibrary`로 승격한다. 입고 위치는 현재 web 단독이므로 `Client/Models`로 충분.

---

## 4. 핵심 아이디어: 버튼 라벨 = 음성 명령 (원안 유지, 프레임워크 무관)

RealWear/Infinity OS 브라우저는 **렌더된 DOM의 버튼 텍스트**를 음성 명령으로 자동 등록한다. 이는 React든 Blazor든 무관 — 최종 HTML만 보면 되기 때문이다. Blazor에서는 `@onclick` 핸들러로 동일하게 동작한다.

```razor
@* 화면에 보이는 이 버튼들이 그대로 음성 명령이 됩니다 *@
<button class="voice-btn" @onclick="ScanBarcode">스캔</button>
<button class="voice-btn" @onclick='() => QueryLocation("입고")'>입고</button>
<button class="voice-btn" @onclick='() => QueryLocation("입고제품")'>입고제품</button>
<button class="voice-btn" @onclick="Show3D">3D 보기</button>
```

- "입고"라고 말하면 해당 버튼의 `@onclick` 실행 — 마우스 클릭과 동일 핸들러
- → **PC 브라우저에서 그대로 개발·디버깅**, 글라스에선 음성으로 동작
- `data-wearhf` 커스텀 속성(비가시 요소 명령)은 Moziware 브라우저 지원 확인 후 옵션. 기본은 "보이는 버튼"으로 충분

---

## 5. 바코드 입력 — `@zxing/browser` + JS interop

Blazor WASM에는 JS 번들러가 없으므로 원안의 `import`를 쓸 수 없다. `@zxing/browser` UMD 번들을 `wwwroot/js/`에 정적 포함하고, 얇은 래퍼를 통해 `IJSRuntime`으로 호출한다. 디코딩 결과는 `DotNetObjectReference` 콜백으로 C#에 돌려준다.

`wwwroot/js/glassScanner.js`:
```javascript
// <script src="_content/.../zxing.min.js"></script> 로 ZXingBrowser 전역 로드 후
let reader, dotnetRef;

export async function start(videoElemId, dotnet) {
  dotnetRef = dotnet;
  reader = new ZXingBrowser.BrowserMultiFormatReader();
  await reader.decodeFromVideoDevice(undefined, videoElemId, (result) => {
    if (result) dotnetRef.invokeMethodAsync('OnBarcode', result.getText());
  });
}
export function stop() { reader?.reset(); }
```

`Pages/Glass.razor`(발췌):
```razor
@inject IJSRuntime JS

<video id="preview" autoplay muted playsinline></video>

@code {
    private IJSObjectReference? _module;
    private DotNetObjectReference<Glass>? _self;
    private string? _lastBarcode;

    private async Task ScanBarcode()
    {
        _self ??= DotNetObjectReference.Create(this);
        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/glassScanner.js");
        await _module.InvokeVoidAsync("start", "preview", _self);
    }

    [JSInvokable]
    public Task OnBarcode(string text)
    {
        _lastBarcode = text;          // 크게 표시 → "입고" 대기
        return InvokeAsync(StateHasChanged);
    }
}
```

- Snapdragon 662 + 16MP면 1D/2D 실시간 디코딩 무리 없음(원안 동일). 레이저 포인터로 조준 보조.
- `IAsyncDisposable`에서 `stop()` 호출 + ref dispose (Andon의 dispose 패턴과 동일).

---

## 6. 인증 — 핸즈프리에 JWT 로그인은 비현실적 → 키오스크 선례 차용

글라스는 키보드/PIN 입력이 어렵다. 기존 `OperatorEndpoints`의 **키오스크 그룹(`/api/kiosk`)** 이 정확한 선례를 제공한다:

```csharp
var kiosk = app.MapGroup("/api/kiosk")
    .AllowAnonymous()
    .AddEndpointFilter<ClientApiKeyEndpointFilter>();
```

- **호환 모드(`ClientApiKey:Required=false`, 기본)**: 헤더 없이 통과 → PoC/현장 도입 단순
- **enforcement(`Required=true`)**: 글라스가 `X-API-Key` 헤더를 실어 호출 → 머신/키오스크와 일관된 보안

본 설계의 `/api/glass`도 동일하게 `.AllowAnonymous().AddEndpointFilter<ClientApiKeyEndpointFilter>()`를 적용한다. (GS 인증 baseline의 X-API-Key 비대칭 원칙과 일치 — `docs/GS_Certification_Baseline.md` §5.1, §11.3 참조.)

> Blazor 클라이언트의 기본 `HttpClient`/`ApiClient`는 JWT 흐름용이다. glass 페이지는 익명 호출이므로 별도 명명 `HttpClient`(또는 `ApiClient.GetAsync` 그대로, 익명 엔드포인트라 401 없음)로 호출하면 된다.

---

## 7. 서버 — Minimal API 엔드포인트 + 서비스

`Endpoints/GlassEndpoints.cs` (기존 `AndonEndpoints`/`OperatorEndpoints` 스타일):
```csharp
public static class GlassEndpoints
{
    public static void MapGlassEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/glass")
            .AllowAnonymous()
            .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        // GET /api/glass/inbound-location?barcode=...&mode=입고
        group.MapGet("/inbound-location", async (
            string barcode, string? mode, IWarehouseService svc) =>
        {
            var loc = await svc.GetInboundLocationAsync(barcode, mode);
            return loc is null
                ? Results.NotFound(new { message = "등록되지 않은 바코드" })
                : Results.Ok(loc);
        });
    }
}
```

`Program.cs`에 한 줄 등록: `app.MapGlassEndpoints();` (다른 `Map*Endpoints()` 호출들과 나란히).

서비스(`IWarehouseService`/`WarehouseService`) — async/await 규칙 준수, EF Core 조회. ⚠️ `string.Join`/문자열 보간은 SQL 로 번역되지 않으므로 **엔티티를 먼저 materialize 한 뒤 메모리에서 위치 문자열을 조립**한다(빈 칸은 건너뜀):
```csharp
public async Task<InboundLocationDto?> GetInboundLocationAsync(string barcode, string? mode = null)
{
    if (string.IsNullOrWhiteSpace(barcode)) return null;

    var item = await _db.WarehouseItems
        .Where(i => i.Barcode == barcode && i.IsActive)
        .FirstOrDefaultAsync();
    if (item is null) return null;

    var parts = new[] { item.Zone, item.Rack, item.Level, item.Bin }
        .Where(s => !string.IsNullOrEmpty(s));
    return new InboundLocationDto
    {
        ItemCode     = item.Code,
        ItemName     = item.Name,
        LocationText = string.Join("-", parts),
        Coord        = new Coord3D { X = item.PosX, Y = item.PosY, Z = item.PosZ }
    };
}
```

DTO(`Client/Models/GlassDto.cs`) — 1·2차 목표 대비 "텍스트 + 좌표" 동시 제공(원안 계약 유지):
```csharp
public class InboundLocationDto
{
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string LocationText { get; set; } = "";
    public Coord3D? Coord { get; set; }
}
public class Coord3D { public double X { get; set; } public double Y { get; set; } public double Z { get; set; } }
```

---

## 8. 데이터 모델 — **신규** (원안이 누락한 최대 작업)

BODA.VMS.Web에는 창고 개념이 없다. 입고 위치 마스터를 신규 정의한다.

`Data/Entities/WarehouseItem.cs`:
```csharp
public class WarehouseItem
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(64)]  public string Barcode { get; set; } = "";   // 조회 키 (인덱스)
    [Required, MaxLength(50)]  public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(20)] public string? Zone { get; set; }
    [MaxLength(20)] public string? Rack { get; set; }
    [MaxLength(20)] public string? Level { get; set; }
    [MaxLength(20)] public string? Bin { get; set; }
    public double PosX { get; set; }  // 2차 3D 하이라이트용 좌표
    public double PosY { get; set; }
    public double PosZ { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

`Program.cs` 부트스트랩(기존 `CREATE TABLE IF NOT EXISTS` 패턴 + 바코드 조회 인덱스):
```sql
CREATE TABLE IF NOT EXISTS "WarehouseItems" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "Barcode" TEXT NOT NULL, "Code" TEXT NOT NULL, "Name" TEXT NOT NULL,
    "Zone" TEXT, "Rack" TEXT, "Level" TEXT, "Bin" TEXT,
    "PosX" REAL NOT NULL DEFAULT 0, "PosY" REAL NOT NULL DEFAULT 0, "PosZ" REAL NOT NULL DEFAULT 0,
    "IsActive" INTEGER NOT NULL DEFAULT 1, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT
);
CREATE INDEX IF NOT EXISTS "IX_WarehouseItems_Barcode" ON "WarehouseItems" ("Barcode");
```

### 8.1 데이터 출처 — 결정됨 (2026-06)

세 가지 후보를 검토했다(상세 트레이드오프는 별도 정리):

| 안 | 진실의 원천 | 적용 |
|----|-------------|------|
| **A. WMS/ERP 동기화** | 외부 ERP (BODA=미러) | **운영 단계 목표** — 고객사에 ERP 존재 |
| **B. 관리 CRUD 수기입력** | BODA 자신 | **만들지 않음** — ERP가 운영 원천이라 수기 유지는 영구히 불필요(버려질 작업) |
| **C. CSV/엑셀 업로드** | 파일 만드는 쪽 | PoC 보강용 + A로 가는 가교(파서는 A 동기화가 재활용) |

**결정:** 고객사 ERP가 존재하므로 **운영 = A(ERP 동기화)**. 단 ERP 연동이 이 기능의 최대 작업이므로 **PoC 단계에서는 BODA 소유(시드, 필요 시 C 업로드)**로 분리해 glass 흐름을 먼저 검증한다. B(수기 CRUD)는 건너뛴다.
→ PoC에서 버려지는 건 **시드 데이터뿐**이고 `WarehouseItem`·`/api/glass`·`/glass`는 운영까지 그대로 직행한다. (배경: 메모리 `warehouse-inbound-location-data-source`)

### 8.2 구현 상태 — PoC 백엔드 완료 ✅ (2026-06)

§7·§8 백엔드가 실제 구현·검증됨:

- `Data/Entities/WarehouseItem.cs` + `BodaVmsDbContext.WarehouseItems` + 바코드 인덱스
- `Program.cs` — 테이블 부트스트랩(위 SQL) + **시드 10건**(`8801234567890`~`...899`, 비어 있을 때만) + DI 등록 + `MapGlassEndpoints()`
- `Services/IWarehouseService`·`WarehouseService`(materialize 후 조립) + `Endpoints/GlassEndpoints.cs`
- `Client/Models/GlassDto.cs`(`InboundLocationDto`+`Coord3D`)
- **단위 테스트 7케이스**(`WarehouseServiceTests`: 존재/미등록/비활성/공백/부분위치) 통과
- end-to-end 확인: `GET /api/glass/inbound-location?barcode=8801234567890` → `A-01-2-07` 반환, 미등록 → 404
- 스파이크 페이지(`wwwroot/glass-spike.html`)의 위치조회가 실제 `/api/glass` 호출로 연결됨

미완(다음 단계): 정식 `/glass` Blazor/경량 페이지(§9, 현재는 스파이크가 대행), 바코드 interop 정식화(§5), C 업로드, 운영 A 동기화.

---

## 9. 화면 레이아웃 — 854×480 단안 (원안 원칙 유지, Blazor로 구현)

- `Pages/Glass.razor`에 `@page "/glass"` + `@layout BODA.VMS.Web.Client.Layout.EmptyLayout` (Andon과 동일한 풀스크린 비-사이드바 레이아웃)
- "1.5초 안에 읽히는 한 화면, 한 정보": 어두운 배경 + 큰 흰/연두 텍스트, 폰트 24px↑, 위치 텍스트는 화면 절반
- 동시에 보이는 음성 버튼 2~3개로 제한
- 854×480 전용 CSS는 컴포넌트 `<style>` 또는 `Glass.razor.css`(scoped)로 — 데스크톱 BODA.VMS.Web과 코드베이스 공유, CSS만 분리

```razor
@page "/glass"
@layout BODA.VMS.Web.Client.Layout.EmptyLayout
@inject ApiClient Api
@inject IJSRuntime JS

@code {
    private async Task QueryLocation(string mode)
    {
        if (string.IsNullOrEmpty(_lastBarcode)) return;
        var data = await Api.GetAsync<InboundLocationDto>(
            $"/api/glass/inbound-location?barcode={_lastBarcode}&mode={mode}");
        if (data is null) { _error = "위치를 찾을 수 없습니다"; }
        else { _locationText = data.LocationText; _coord = data.Coord; } // 2차용 좌표 보관
        await InvokeAsync(StateHasChanged);
    }
}
```

---

## 10. 2차 목표(3D) — Three.js를 JS interop로 신규 도입

원안과 결론은 같다(**Unity 아님, Three.js**). 단, *기존 자산 재활용은 불가* — 이 저장소엔 Three.js가 없다. 그러므로:

- `wwwroot/js/`에 Three.js + glTF 로더를 정적 포함, `glassViewer.js` 모듈로 래핑
- `/glass` 페이지가 §7 응답 좌표(`Coord3D`)를 interop로 넘겨 해당 bin 하이라이트
- 단안 화면 목표는 몰입형 VR이 아니라 **"회전·확대 가능한 3D 위치 뷰어"** (원안 §9 동일)
- 도면(glTF/평면 메시) 제작·좌표계 정합이 별도 작업으로 필요 (디지털 트윈을 처음부터)

---

## 11. 리스크 / 검증 포인트

원안의 유효 항목 + Blazor 특이사항:

- **한국어 단음절 오인식**: "입고" 같은 짧은 명령은 소음 환경에서 오인식 가능 → "입고 제품 조회"처럼 음절 늘리거나 동의어 버튼 복수 등록. **실기 검증 필수**
- **단안 854×480 / 20° FOV**: 몰입형 VR·공간정합 AR 불가 → 작은 3D 위치 뷰어가 현실적 목표
- **카메라 바코드 성능**: 실제 창고 조도·손상 바코드 인식률 확인
- **(Blazor) WASM 첫 로드 용량**: glass 페이지는 가벼우나 WASM 런타임 초기 다운로드가 있음 → 기기에서 최초 로딩 시간 측정. 글라스 캐시/오프라인 고려
- **(Blazor) JS interop 카메라 권한**: WASM에서 `getUserMedia` 권한·HTTPS 요구 — 기기 브라우저 권한 흐름 확인
- **(데이터) 입고 위치 출처 미확정**(§8) — 기술 리스크보다 선행하는 **업무/데이터 리스크**

---

## 12. 만들기 순서 (Blazor 기준 재정의)

1. **시드 데이터 + `/glass` mock**: `WarehouseItems` 테이블 + 시드 몇 건, `/glass` 라우트와 버튼 핸들러로 전체 흐름을 PC 브라우저에서 완성 (Api 호출은 실제 엔드포인트, 데이터는 시드)
2. **바코드 interop**: `glassScanner.js` + `@zxing/browser` 정적 포함, PC 웹캠으로 디코딩 검증
3. **글라스 실기 검증** ← 첫 실물 포인트: **한국어 음성 인식률 + 카메라 바코드 성능 + WASM 로딩 시간**
4. **데이터 출처 확정·연동**(§8): WMS/ERP 동기화 또는 관리 CRUD — *운영 도입 전 필수*
5. **1차(텍스트) 안정화 후** Three.js 3D 뷰어(§10) 추가

---

## 13. 도입 판정 요약

- **가능 여부:** ✅ 가능 + **PoC 백엔드 구현·검증 완료**(§8.2). 기존 Andon/키오스크 패턴 위에 `/glass` + `/api/glass`를 얹는 구조는 이 저장소와 잘 맞는다.
- **신규 안드로이드 앱은 아니다 → 기존 Blazor 호스트 확장.** 서버 작업(엔티티·엔드포인트·데이터 출처)은 프런트가 웹이든 네이티브든 동일하므로, 단순 "스캔→조회→표시"에 두 번째 코드베이스/언어/배포는 과하다.
- **데이터 출처 결정됨(§8.1):** 운영=고객사 ERP 동기화(A), PoC=BODA 소유 시드(+필요 시 C), 수기 CRUD(B)는 안 만듦.
- **프런트 권고 — 비-WASM 경량 페이지:** glass 화면은 "스캔+버튼 3개+텍스트"로 단순하므로, 굳이 Blazor WASM 컴포넌트(§9 스니펫)로 만들 필요 없이 **같은 호스트에서 서버 렌더 Razor Page 또는 정적 HTML+JS로 `/glass`를 서빙**하면 WASM 런타임 다운로드를 통째로 건너뛴다(기기 제약상 유리). 현재 스파이크(`glass-spike.html`)가 바로 이 비-WASM 형태의 프로토타입이다. §9의 WASM 스니펫은 대안으로만 유지.
- **남은 진짜 게이트(기술):** "WASM"이 아니라 **임베디드 브라우저의 바코드 입력 방식**. 1순위 검증은 **기기 하드웨어 스캐너가 웹 input에 값을 넣어주는가(키보드-웨지)** — 되면 `getUserMedia`/카메라 디코딩 의존이 사라진다. 차선 getUserMedia+디코딩, 최후 얇은 네이티브 WebView 브리지. (스파이크 페이지가 이 순서대로 검증)
- **네이티브가 정당화되는 조건:** 손상/저조도 라벨을 하드웨어 스캐너급으로 읽어야 함 · WiFi 음영 오프라인 필수 · 스파이크에서 브라우저가 카메라/바코드를 못 돌림.
- **다음 1순위 행동:** 온디바이스 스파이크로 위 게이트 확인(실데이터까지 붙어 "스캔→입고→위치" end-to-end 검증 가능).
