using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Analytics;

// 날짜 범위 분석 요청 DTO 들 — StartDate / EndDate / Optional ClientId 패턴 공통.
// 각각 별도 파일로 분리하지 않고 묶음 — 검증 규칙이 거의 동일.

public sealed class ReliabilityRequestDtoValidator : AbstractValidator<ReliabilityRequestDto>
{
    public ReliabilityRequestDtoValidator()
    {
        ApplyDateRange(this);
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);
    }

    private static void ApplyDateRange(AbstractValidator<ReliabilityRequestDto> v)
    {
        v.RuleFor(x => x.StartDate).NotEqual(default(DateTime)).WithMessage("StartDate is required");
        v.RuleFor(x => x.EndDate).NotEqual(default(DateTime)).WithMessage("EndDate is required");
        v.RuleFor(x => x)
            .Must(r => r.StartDate <= r.EndDate)
            .WithMessage("StartDate must be <= EndDate");
    }
}

public sealed class OeeRequestDtoValidator : AbstractValidator<OeeRequestDto>
{
    public OeeRequestDtoValidator()
    {
        RuleFor(x => x.StartDate).NotEqual(default(DateTime)).WithMessage("StartDate is required");
        RuleFor(x => x.EndDate).NotEqual(default(DateTime)).WithMessage("EndDate is required");
        RuleFor(x => x)
            .Must(r => r.StartDate <= r.EndDate)
            .WithMessage("StartDate must be <= EndDate");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);
    }
}

public sealed class ShiftReportRequestDtoValidator : AbstractValidator<ShiftReportRequestDto>
{
    public ShiftReportRequestDtoValidator()
    {
        RuleFor(x => x.StartDate).NotEqual(default(DateTime)).WithMessage("StartDate is required");
        RuleFor(x => x.EndDate).NotEqual(default(DateTime)).WithMessage("EndDate is required");
        RuleFor(x => x)
            .Must(r => r.StartDate <= r.EndDate)
            .WithMessage("StartDate must be <= EndDate");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);
    }
}

public sealed class ReportRequestDtoValidator : AbstractValidator<ReportRequestDto>
{
    public ReportRequestDtoValidator()
    {
        RuleFor(x => x.ReferenceDate)
            .NotEqual(default(DateTime)).WithMessage("ReferenceDate is required");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);
        RuleFor(x => x.Type).IsInEnum().WithMessage("Type must be a valid ReportType enum");
    }
}

public sealed class SpcRequestDtoValidator : AbstractValidator<SpcRequestDto>
{
    public SpcRequestDtoValidator()
    {
        RuleFor(x => x.RecipeId).GreaterThan(0).WithMessage("RecipeId must be > 0");
        RuleFor(x => x.ParamCode).GreaterThan(0).WithMessage("ParamCode must be > 0");

        RuleFor(x => x.SubgroupSize)
            .InclusiveBetween(2, 10).WithMessage("SubgroupSize must be 2-10");

        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);
        RuleFor(x => x.WorkOrderId).GreaterThan(0).When(x => x.WorkOrderId.HasValue);

        // 둘 다 지정시 정합성
        RuleFor(x => x)
            .Must(r => !(r.StartDate.HasValue && r.EndDate.HasValue)
                      || r.StartDate <= r.EndDate)
            .WithMessage("StartDate must be <= EndDate");
    }
}
