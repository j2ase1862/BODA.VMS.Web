namespace BODA.VMS.Web.Middleware;

/// <summary>
/// appsettings "ClientApiKey" 섹션 옵션.
/// - Value: 클라이언트가 X-API-Key 헤더로 보내야 하는 비밀 키
///   (소스 저장 금지 — user-secrets / 환경변수 ClientApiKey__Value)
/// - Required: true 면 모든 익명 머신 endpoint 가 X-API-Key 헤더 강제 (401).
///   false (기본) 면 헤더가 있으면 검증, 없으면 통과 — 기존 VMS 클라이언트 호환.
///   GS 인증 운영 전환 시점에 true 로 변경.
/// </summary>
public sealed class ClientApiKeyOptions
{
    public const string SectionName = "ClientApiKey";
    public const string HeaderName = "X-API-Key";

    public string? Value { get; set; }
    public bool Required { get; set; } = false;
}
