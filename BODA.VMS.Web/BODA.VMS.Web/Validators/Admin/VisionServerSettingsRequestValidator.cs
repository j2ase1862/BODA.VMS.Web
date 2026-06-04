using BODA.VMS.Web.Endpoints;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Admin;

/// <summary>
/// /api/settings/visionserver PUT 본문 검증. BaseUrl 은 절대 http(s) URL 만 허용 —
/// 잘못된 값이 appsettings.json 에 영구 기록되면 운영 복구에 수동 편집 필요.
/// </summary>
public sealed class VisionServerSettingsRequestValidator : AbstractValidator<VisionServerSettingsRequest>
{
    public VisionServerSettingsRequestValidator()
    {
        // BaseUrl 은 선택값 — null/빈 문자열은 허용 (Enabled=false 시나리오).
        // 값이 있으면 절대 http(s) URL 이어야 함.
        RuleFor(x => x.BaseUrl)
            .Must(BeValidAbsoluteHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.BaseUrl))
            .WithMessage("BaseUrl 은 절대 http(s) URL 이어야 합니다 (예: http://localhost:5000).");

        RuleFor(x => x.BaseUrl)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.BaseUrl));
    }

    private static bool BeValidAbsoluteHttpUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
