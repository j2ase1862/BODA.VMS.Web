# Changelog — BODA.VMS.Web

## v1.1.0 (2026-05-22)

### 🚀 VMS 통합 (Phase 7 — 운영 워크플로)

- **Stage 2 — `/api/workorders/by-client/{clientIndex}` 익명 endpoint** — VMS 가 Operator 로그인 후 작업지시 목록을 가져올 때 사용. 인증 그룹과 분리, 옵션 `status` 쿼리.
- **Stage 3 — `/api/parameters/results` 응답 확장**:
  - WO 진행률 dict (`producedQuantity`/`passQuantity`/`ngQuantity`/`status`/`completed`) 추가
  - Planned → InProgress 자동 전이 (`ActualStartAt` 자동 설정)
  - `ProducedQuantity >= PlannedQuantity` 도달 시 Completed + `ActualEndAt` 자동 설정
- **B1 — `/api/lots/active-by-workorder/{woId}` 익명 endpoint** — WO 의 활성(Open) Lot 1개 조회. VMS 가 WO 선택 시 자동 채움.
- **C5 — `Hubs/VmsPublicHub` 익명 SignalR Hub** — `/hubs/vms-public`. `WorkOrderUpdated` / `WorkOrderCompleted` broadcast.
- **D10 — `Operator.Role` 필드 + DTO 전파**:
  - 엔티티에 `Role` (NOT NULL, DEFAULT 'Operator') + 자동 마이그레이션 (PRAGMA + ALTER TABLE)
  - `OperatorDto` / `OperatorUpsertDto` / `OperatorSessionDto` 에 Role 전파
  - `OperatorService.NormalizeRole` 정규화
  - `OperatorFormDialog.razor` Role MudSelect 드롭다운

### 🔔 알림 시스템 개선

- **NotificationBell 배지 가시성**: `Origin.TopLeft` + 커스텀 CSS — AppBar 모서리 잘림 해소. `Max=99` 로 100+ 시 "99+" 표시.
- **알림 확인/삭제 기능**:
  - 메뉴 헤더 "모두 확인" 버튼 (`IAlarmService.AcknowledgeAllAsync` + `POST /api/alarms/acknowledge-all`)
  - 각 알림 우측 X 아이콘 — 개별 확인 (기존 action endpoint 재사용)
  - 감사 추적성 유지 (Delete 가 아닌 Acknowledge)

### 👥 Operator Sessions 실시간 갱신

- **OperatorSessionService** 가 `IHubContext<VmsHub>` 주입 → `OperatorSessionStarted` / `OperatorSessionEnded` broadcast.
- **SignalRService.cs** 두 이벤트 노출.
- **Operators.razor** Sessions 탭이 두 이벤트 구독 → `LoadSessions` 자동 호출. 새로 고침 불필요.

### 🐛 Stale Session 정리

- **VMS 비정상 종료 시 "계속 작업중" 표시 해결**:
  - `/api/clients/disconnect` 에서 활성 OperatorSession 자동 종료 (`SessionEndReason.Disconnect`) + SignalR broadcast
  - Web startup 시 heartbeat 5분 이상 끊긴 클라이언트의 활성 세션 일괄 `Stale` 정리

### 📦 배포

- **PowerShell 배포 스크립트**: `deploy/Install-Web.ps1` (Kestrel + Windows Service + 방화벽), `deploy/Uninstall-Web.ps1`.
- 공용 버전 관리: 솔루션 루트 `Directory.Build.props`.

### 🔗 통합 정렬

- VMS ↔ Web DTO 양방향 호환 (`WorkOrderDto` / `LotDto` / `OperatorSessionDto` Role 포함 / `WorkOrderProgressDto`).
- VMS 운영 endpoint 모두 익명 (`.AllowAnonymous()`) — JWT 없이 동작.

---

## v1.0.0

초기 릴리스. Phase 1~6 통합 contract 정렬.
