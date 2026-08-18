using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Inspection;

public sealed class ParameterResultUploadRequestValidator : AbstractValidator<ParameterResultUploadRequest>
{
    public ParameterResultUploadRequestValidator()
    {
        RuleFor(x => x.ClientIndex)
            .GreaterThanOrEqualTo(0).WithMessage("ClientIndex must be >= 0");

        RuleFor(x => x.RecipeId)
            .GreaterThan(0).WithMessage("RecipeId must be > 0");

        RuleFor(x => x.Results)
            .NotNull().WithMessage("Results is required")
            .Must(r => r.Count <= 1000).WithMessage("Results count must not exceed 1000 per upload");

        // 판정 전용(사이클) 업로드는 OverallPass 만으로 접수 — 그 외에는 기존과 동일하게
        // 측정값 1건 이상을 요구한다 (구버전 VMS 계약 유지).
        RuleFor(x => x.Results)
            .Must(r => r.Count > 0)
            .When(x => !x.OverallPass.HasValue)
            .WithMessage("Results must contain at least 1 entry (or provide OverallPass)");

        RuleForEach(x => x.Results).SetValidator(new ParameterResultDtoValidator());

        // 추적성 필드: nullable 이지만 값이 있으면 형식 검증
        RuleFor(x => x.SerialNumber)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.SerialNumber));

        // 예측 피처 범위 (V1/V2/V3) — 값이 있을 때만 검증
        RuleFor(x => x.Brightness)
            .InclusiveBetween(0, 255).When(x => x.Brightness.HasValue);

        RuleFor(x => x.DlConfidence)
            .InclusiveBetween(0, 1).When(x => x.DlConfidence.HasValue);

        RuleFor(x => x.CycleTimeMs)
            .GreaterThan(0).When(x => x.CycleTimeMs.HasValue);

        RuleFor(x => x.DlModelVersion)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.DlModelVersion));
    }
}

internal sealed class ParameterResultDtoValidator : AbstractValidator<ParameterResultDto>
{
    public ParameterResultDtoValidator()
    {
        RuleFor(x => x.ParamCode)
            .GreaterThan(0).WithMessage("ParamCode must be > 0");

        RuleFor(x => x.Judgment)
            .NotEmpty().WithMessage("Judgment is required")
            .MaximumLength(20);
    }
}
