# BODA.VMS.Web — 불량률 예측(Predictive Defect Rate) 도입 계획

> **작성**: 2026-05-26
> **읽는 대상**: BODA.VMS.Web 측 + VMS(.NET 8 WPF) 측 Claude Code 에이전트
> **목표**: 검사 시계열·파라미터·설비 데이터로부터 **차기 단위 시간(예: 다음 1시간/다음 Lot)의 불량률(0~1 회귀)**을 예측하고, 결과를 Web UI/Andon/Alarm 파이프라인에 통합한다.
> **위치**: 스마트팩토리 Level 2 → Level 3 도약을 위한 첫 번째 AI 능력.

---

## 1. 왜 "불량률 예측"부터인가

| 기준 | 평가 |
|---|---|
| **데이터 가용성** | `InspectionHistory` + `ParameterMeasurement` 이미 양호 |
| **라벨 명확성** | `IsNg`(이진) + `DefectCode`(다중) → 회귀 타깃 산출 쉬움 |
| **검증 용이성** | 다음 윈도우 실측치가 1시간 내 확보 → 빠른 피드백 루프 |
| **사업 가치** | SPC 선행 경보 → 불량 발생 *전* 개입 가능 |
| **확장성** | 이 인프라가 완성되면 RUL/Cpk Drift/OEE 예측을 같은 골격에 얹을 수 있음 |

---

## 2. 예측 스펙 정의

### 2.1 타깃(Y)
- **정의**: 특정 `(VisionClient, Recipe)` 키에 대해 **이후 N분 윈도우의 NG 비율 (0~1)**
- **권장 윈도우**: N=60분 (교대 단위 너무 길고, 분 단위는 노이즈 큼)
- **회귀 손실**: MSE 또는 Huber. 클래스 불균형이 심하면 *quantile regression* (P50/P90) 병행

### 2.2 입력 피처(X) — 4 카테고리

| 카테고리 | 출처 | 예시 |
|---|---|---|
| **(A) 검사 통계** | `InspectionHistory` (집계) | 직전 1h/4h/24h NG율, Lot 진행률, 누적 검사수 |
| **(B) 파라미터 시계열** | `ParameterMeasurement` | ParamCode별 평균/표준편차/Cpk, 직전값과의 delta, USL/LSL 근접도 |
| **(C) 설비/환경** | `EquipmentStatusLog`, `AlarmEvent`, `MaintenanceRecord` | 최근 다운타임, 알람 빈도, 마지막 PM 이후 경과시간 |
| **(D) 컨텍스트** | `Shift`, `Operator`, `Product`, 시간 | 교대조, 작업자 ID, 제품군, 요일/시각, 가동 누적시간 |

### 2.3 학습 단위
- **레코드 = (ClientIndex, RecipeId, 시간 버킷 60분) 한 행**
- 데이터셋 구축은 Feature View(섹션 4)에서 일괄.

---

## 3. 데이터 준비 현황 점검

### 3.1 ✅ 이미 있는 자산
- `InspectionHistory` (IsNg, DefectCode, Timestamp, WorkOrderId, LotId, OperatorId, ClientId, RecipeId)
- `ParameterMeasurement` (ParamCode, MeasuredValue, Judgment, Timestamp + HistoryId FK)
- `EquipmentStatusLog`, `AlarmEvent`, `MaintenanceRecord/Schedule`
- `Shift`, `Operator`, `Product` 마스터

### 3.2 ⚠️ 보강 필요 — 우선순위 순

| ID | 항목 | 책임 | 사유 |
|---|---|---|---|
| **D1** | `ParameterMeasurement`에 **USL/LSL 정상치 컬럼 동결** (스냅샷) | Web | 학습 시점의 한계값을 알아야 "한계 근접도" 피처를 만들 수 있음. RecipeParameter는 변경됨 → 시점 동결 필요 |
| **D2** | **`SensorReading` 엔티티 신설** (Temperature/Humidity/Vibration/PressurePsi/Timestamp, ClientId FK) | Web 스키마 + VMS 수집 | 환경 변수가 가장 강한 예측 피처. 현재 미수집 |
| **D3** | **이미지 품질 메트릭** (`brightness`, `contrastStd`, `focusScore`, `blobCount`, `maxBlobArea`) 를 `InspectionHistory`에 추가 | VMS 측 (OpenCvSharp/NativeVision에서 산출) + Web 스키마 | OK 안에서도 드리프트를 잡아내는 핵심 피처 |
| **D4** | **CycleTimeMs / TactMs** 컬럼 추가 | VMS → 업로드 DTO 확장 | 가동 페이스 둔화가 곧 품질 저하 선행 신호 |
| **D5** | **DL 모델 신뢰도 점수** (이미 VMS NativeVision DL 사용 시) | VMS 업로드 DTO 추가 | 분류 신뢰도가 임계 근처일수록 잠재 NG |
| **D6** | **Feature View `v_defect_features`** SQL VIEW | Web | 학습·추론·디버깅이 같은 정의를 공유해야 재현성 확보 |

### 3.3 데이터 양·품질 사전 점검 체크리스트
- [ ] 최소 **3개월치 InspectionHistory** 누적 (6개월 권장)
- [ ] **NG율 > 0.2%**, < 50% (한쪽으로 극단치 모이면 회귀 부적합)
- [ ] **ParameterMeasurement NULL 비율** 컬럼별 측정
- [ ] **DefectCode 라벨링 일관성** — 미등록 코드/오타 정리 (이미 자동완성 도입됨 ✓)

---

## 4. 아키텍처 — 권장안: Python 학습 + ONNX in-proc 추론

```
┌──────────────────────────┐         ┌──────────────────────────┐
│ VMS (.NET 8 WPF)         │         │ BODA.VMS.Web             │
│  • 검사 실행             │  HTTP   │  • REST API              │
│  • 파라미터/이미지/센서  │ ──────▶ │  • Feature View          │
│  • CycleTime, 신뢰도     │ POST    │  • IPredictionService    │
└──────────────────────────┘ /api/   │      └─ ONNX Runtime     │
                             results │  • 예측 결과 저장        │
                                     │  • Blazor /forecast 페이지│
                                     └──────────┬───────────────┘
                                                │
                                                │ 야간 batch / 주간 cron
                                                ▼
                                     ┌──────────────────────────┐
                                     │ ML Trainer (Python)      │
                                     │  • SQLite 읽기 (read-only)│
                                     │  • LightGBM baseline →   │
                                     │    TabTransformer/LSTM    │
                                     │  • MLflow 실험관리       │
                                     │  • ONNX export → models/ │
                                     └──────────────────────────┘
```

### 4.1 선택 근거
- **학습은 Python**: 빠른 실험, LightGBM/PyTorch 생태계
- **추론은 .NET ONNX Runtime**: 솔루션 컴포넌트 +0, 저지연(< 5ms), 배포 단순
- **DB는 그대로 SQLite**: 학습기는 read-only로 붙음. WAL 모드이므로 운영 영향 없음

### 4.2 모델 메타 테이블 (신규)
```csharp
public class MLModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";   // "defect_rate_60m"
    public string Version { get; set; } = ""; // "2026.05.26-r1"
    public string OnnxPath { get; set; } = "";
    public double Mae { get; set; }
    public double R2 { get; set; }
    public DateTime TrainedAt { get; set; }
    public bool IsActive { get; set; }
    public string FeatureSpecJson { get; set; } = ""; // 피처 순서·정규화 통계
}

public class PredictionLog  // 드리프트 모니터링용
{
    public int Id { get; set; }
    public int MLModelId { get; set; }
    public int ClientId { get; set; }
    public int RecipeId { get; set; }
    public DateTime WindowStart { get; set; }
    public double PredictedNgRate { get; set; }
    public double? ActualNgRate { get; set; } // 윈도우 종료 후 backfill
    public DateTime CreatedAt { get; set; }
}
```

---

## 5. VMS 측 병행 작업 (★ 본 문서의 핵심)

> **전제**: VMS는 .NET 8 WPF, OpenCvSharp4 + NativeVision C++ DLL 사용, Web과는 HTTP REST 단방향.
> 따라서 VMS는 **피처 생산자(producer)** 역할만 강화하면 됨. 추론·UI는 모두 Web 책임.

### 5.1 업로드 DTO 확장 (`ParameterResultUploadRequest`)

현재 contract에 **이미지 품질·사이클타임·DL 신뢰도** 필드를 추가:

```csharp
public class ParameterResultUploadRequest
{
    public int ClientIndex { get; set; }
    public int RecipeId { get; set; }
    public List<ParameterResultDto> Results { get; set; } = new();

    public int? WorkOrderId { get; set; }
    public int? LotId { get; set; }
    public int? OperatorId { get; set; }
    public string? SerialNumber { get; set; }

    // ─── 신규 (예측 피처용) ───
    public int? CycleTimeMs { get; set; }       // 검사 1회 소요 ms
    public double? Brightness { get; set; }     // 이미지 평균 밝기 (0~255)
    public double? ContrastStd { get; set; }    // 표준편차
    public double? FocusScore { get; set; }     // Laplacian variance 등
    public int? BlobCount { get; set; }         // 검출 blob 개수
    public double? MaxBlobAreaPx { get; set; }
    public double? DlConfidence { get; set; }   // NativeVision DL 모델 신뢰도 0~1
    public string? DlModelVersion { get; set; }
}
```

→ Web 측에서는 이 필드들을 `InspectionHistory`에 nullable로 추가하여 마이그레이션. **하위 호환** 유지(VMS 미업데이트 시 NULL).

### 5.2 환경 센서 수집 (`/api/sensors` 신규 엔드포인트)

```
POST /api/sensors/readings
{
  "clientIndex": 1,
  "timestamp": "2026-05-26T10:30:00Z",
  "temperatureC": 24.3,
  "humidityPct": 48.1,
  "vibrationRms": 0.12,
  "pressurePsi": null
}
```

- VMS 측: Heartbeat와 동일한 주기(5초)로 PLC/센서 모듈에서 읽어 송신
- Web 측: `SensorReading` 엔티티 신설, 시계열 저장(InspectionHistory와 동일하게 Audit 제외)
- 센서 미연결 환경: 호출 안 함 → Web에서 NULL 허용

### 5.3 VMS UI에 예측 결과 표시 (선택, Phase 후반)

Web의 `GET /api/predictions/current/{clientIndex}` 응답을 VMS 메인 화면 상단에 작은 위젯으로 노출:
- 예: "다음 1시간 예상 NG율: **2.4%** (정상)"
- 임계 초과 시 색 변경 + 작업자 주의 메시지

→ VMS의 기존 ParameterSyncService 패턴과 동일하게 폴링하면 됨.

### 5.4 VMS 측 작업 우선순위 정리

| # | 작업 | 난이도 | 의존성 |
|---|---|---|---|
| V1 | 이미지 품질 메트릭(Brightness/Contrast/Focus/Blob) 산출 및 DTO 확장 | 중 | OpenCvSharp 함수 호출 추가 |
| V2 | CycleTimeMs 측정 및 송신 | 하 | Stopwatch 한 줄 |
| V3 | DL 신뢰도 점수 송신 (NativeVision DL 사용 시) | 중 | NativeVision API 노출 여부 확인 |
| V4 | `/api/sensors/readings` 클라이언트 호출 | 중 | PLC/센서 모듈 연결 상태에 의존 |
| V5 | 예측 결과 위젯 (UI) | 하 | Web API 완성 후 |

---

## 6. Web 측 작업 로드맵

### Phase A — 스키마 보강 (1주)
- [ ] `InspectionHistory`에 V1~V3 신규 컬럼 nullable 추가 + EF 마이그레이션
- [ ] `SensorReading` 엔티티 신설
- [ ] `MLModel`, `PredictionLog` 엔티티 신설
- [ ] `/api/sensors/readings` 엔드포인트 (POST, anonymous-but-rate-limited 또는 ClientCert)
- [ ] `ParameterResultUploadRequest` 후방 호환 처리

### Phase B — Feature View + 데이터 익스포트 (1주)
- [ ] SQL VIEW `v_defect_features` 정의 — (ClientId, RecipeId, hour) 그룹의 모든 피처 집계
- [ ] `tools/export_training_data.py` — SQLite read-only로 Parquet 추출
- [ ] 데이터 품질 리포트 (`notebooks/00_data_quality.ipynb`) — NULL 비율, 라벨 분포, 시계열 갭

### Phase C — 베이스라인 모델 (1~2주)
- [ ] **LightGBM baseline** (시간 기반 train/val split)
- [ ] 평가지표: MAE / R² / Quantile loss
- [ ] **Permutation feature importance** → 어떤 피처가 실제로 도움 되는지 검증
- [ ] 베이스라인 MAE를 결과 보고. **이 단계에서 멈출지 딥러닝 갈지 결정**

### Phase D — 딥러닝 모델 (조건부, 2주)
- 조건: 베이스라인 R² > 0.3 (의미 있는 신호 존재 확인) **AND** 피처 6개 이상 중요도 > 0.05
- 후보: **TabTransformer** (정형 데이터에서 강함) → **LSTM/Temporal Fusion Transformer** (시계열 강조 시)
- ONNX export 검증

### Phase E — 운영 통합 (1~2주)
- [ ] `IPredictionService` (.NET) — ONNX Runtime 추론, 캐싱 60초
- [ ] `/api/predictions/current/{clientIndex}` GET
- [ ] Blazor 페이지 `/forecast` — 라인별 예측 카드 + 시계열 차트
- [ ] **Andon 통합**: 예측 NG율 > 임계 시 사전 경보 카드 표시
- [ ] **Alarm 통합**: 임계 초과 시 ISA-18.2 알람 발생(`PredictiveAlarm` 카테고리)

### Phase F — 재학습 + 드리프트 모니터링 (지속)
- [ ] 주간 cron으로 재학습 (Task Scheduler 또는 hangfire)
- [ ] `PredictionLog.ActualNgRate` 백필 (윈도우 종료 1시간 후)
- [ ] `/forecast` 페이지에 예측 vs 실측 잔차 SPC 차트
- [ ] 잔차가 일정 임계 넘으면 재학습 자동 트리거

---

## 7. 총 일정 (병행 작업 기준 8~10주)

```
Week  1  2  3  4  5  6  7  8  9  10
Web   [A───][B───][C───────][E─────][F (지속)
VMS   [V1·V2][V3·V4]              [V5]
ML    .....[Baseline][DL? ][ONNX ]
```

VMS 측 V1·V2(이미지 품질·사이클타임)는 Web Phase A와 동시에 가능 → 학습 데이터에 즉시 반영.

---

## 8. 열린 질문 / 결정 필요

1. **윈도우 길이 N=60분 vs 30분 vs Lot 단위** — 운영팀과 협의 (현장 의사결정 호흡)
2. **VMS NativeVision DL 신뢰도 점수가 노출 가능한가?** — VMS 측 확인 필요
3. **센서 모듈 보유 현황** — 현장별 편차. 없는 경우 D2(SensorReading)는 후순위로
4. **예측 결과를 누가 보는가?** — 작업자(VMS UI)? 라인장(Andon)? 관리자(Web `/forecast`)? UI 우선순위 결정
5. **임계 초과 시 알람을 만들 것인가, 추천만 할 것인가?** — 오탐 시 알람 피로 위험 → 초기에는 "Predictive Insight" 별도 채널 권장

---

## 9. 성공 기준

- **기술**: 60분 단위 NG율 회귀에서 MAE < 0.02 (현장 NG율 평균이 1~3% 가정)
- **운영**: 예측 알람 → 실제 NG 발생 사이 평균 리드타임 > 15분
- **사업**: 도입 후 3개월간 월간 NG율 상대 감소 ≥ 10% (선제 개입 효과)

---

## 10. 다음 액션 (즉시)

1. **Web 측**: Phase A 스키마 마이그레이션 PR 초안 작성
2. **VMS 측**: V1(이미지 품질 메트릭) 함수 PoC — 단일 검사에서 4개 값 산출되는지 확인
3. **공통**: 위 "열린 질문" 5개에 대해 운영팀과 30분 미팅
