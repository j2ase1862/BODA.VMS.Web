[Project Context] BODA VMS (Vision Management System)
이 문서는 BODA VMS 프로젝트의 아키텍처, 기술 스택 및 개발 규칙을 정의합니다. AI 에이전트는 코드를 생성하거나 수정할 때 본 문서의 지침을 반드시 준수해야 합니다.

1. 시스템 아키텍처 (Side-by-Side Design)
시스템은 안정적인 비전 검사와 현대적인 웹 관리를 분리한 하이브리드 구조를 가집니다.

Vision Engine: .NET Framework 4.8.1 기반. Cognex VisionPro 라이브러리를 사용하며 실시간 검사 및 데이터 기록을 담당합니다.

Web Solution: .NET 8 기반 Blazor Web App. 대시보드, 이력 조회 및 클라이언트 관리를 담당합니다.

Shared Data: SQLite (BodaVision.db)를 통해 두 프로세스가 통신하며, 성능을 위해 WAL(Write-Ahead Logging) 모드를 사용합니다.

2. 솔루션 및 프로젝트 구조
모든 공유 데이터 객체는 NuGet 패키지화를 염두에 둔 .NET Standard 2.0 라이브러리를 통해 관리됩니다.

2.1 BODA.VMS.ShareLibrary (.NET Standard 2.0)
목적: 모든 솔루션(Server, Client, Web)에서 공통으로 참조하는 NuGet 패키지 대상.

포함 내용: DTO(Data Transfer Objects), Enum, 공통 상수, 인터페이스.

제약: Cognex VisionPro 등 특정 프레임워크 종속적인 라이브러리는 포함하지 않음.

2.2 BODA.VMS.SERVER (.NET Framework 4.8.1)
역할: 비전 서버 엔진. 7개 비전 툴(PMAlign, Blob, Caliper 등) 연산 수행.

특징: Windows Service로 등록되어 배포됨.

2.3 BODA.VMS.CLIENT (.NET Framework 4.8.1)
역할: 운영 UI (WPF). 실시간 이미지 표시 및 로컬 결과 처리.

2.4 BODA.VMS.Web (.NET 8)
구조: Blazor Web App (WebAssembly 대화형 모드).

Backend: SQLite DB 접근 및 RESTful API 제공.

Frontend: 대시보드, 이력 조회, 클라이언트 관리 UI.

3. 데이터베이스 사양 (SQLite)
DB 파일: BodaVision.db

핵심 설정:

PRAGMA journal_mode=WAL; (10개 클라이언트 동시 쓰기 대응)

PRAGMA foreign_keys = ON; (데이터 무결성 유지)

테이블: Clients, Recipes, Cameras, Steps, InspectionTools, Users 등.

4. AI 개발 및 코딩 규칙 (Essential)
4.1 성능 최적화 (Tact-time)
이미지 전송: 서버-클라이언트 간 이미지 전송 시 반드시 JPEG(품질 90 이상) 포맷을 사용합니다. (BMP 사용 금지)

그래픽 처리: 클라이언트 디스플레이에서 수백 개의 Blob 박스를 그리지 않습니다. 검사 영역(Region)과 요약 텍스트(Label) 위주로 생성하여 COM 오버헤드를 최소화합니다.

DB 쓰기: 대량의 데이터 기록 시 BlockingCollection<T>을 이용한 비동기 큐잉(Producer-Consumer) 패턴을 사용하여 비전 검사 스레드에 영향을 주지 않아야 합니다.

4.2 기술적 제약
참조 관계: Web 프로젝트(.NET 8)에서 ShareLibrary 외의 .NET Framework DLL을 직접 참조하지 않도록 주의합니다.

비동기 처리: 웹 API 및 DB I/O 작업은 항상 async/await를 사용하여 논블로킹(Non-blocking)으로 구현합니다.

5. 웹 페이지 주요 기능 (Web Specification)
생산 이력 조회: 날짜별 통계 및 특정 항목 클릭 시 NG 이미지/수치 팝업 표시.

로그인/보안: 관리자 승인 기반 계정 생성 및 JWT 인증 적용.

클라이언트 관리: 10개 클라이언트의 IP, 이름, 순번(Index) 관리 및 실시간 상태(Alive) 체크.

[AI 명령 시 팁]
"새로운 DTO를 추가해줘" -> BODA.VMS.ShareLibrary 프로젝트에 생성.

"검사 결과 저장 로직을 짜줘" -> SQLite WAL 모드와 비동기 큐 방식을 적용.

"웹 화면을 만들어줘" -> MudBlazor 스타일의 사이드바 레이아웃과 Blazor WASM 모드 적용.