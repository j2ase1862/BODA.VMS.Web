using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Alarm;

public sealed class AlarmActionRequestValidator : AbstractValidator<AlarmActionRequest>
{
    private static readonly string[] AllowedActions = { "Acknowledge", "Resolve" };

    public AlarmActionRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty().WithMessage("Action is required")
            .Must(a => AllowedActions.Contains(a))
            .WithMessage($"Action must be one of: {string.Join(", ", AllowedActions)}");

        // Resolve 시 Resolution 필수 — 별도 RuleFor 로 분리 (체인 시 두번째 When 이
        // 첫 When 까지 영향을 미쳐 NotEmpty 가 절대 발화하지 않는 버그 방지).
        RuleFor(x => x.Resolution)
            .NotEmpty()
            .When(x => x.Action == "Resolve")
            .WithMessage("Resolution is required when Action is 'Resolve'");

        // 길이 검증 — 값이 있을 때만
        RuleFor(x => x.Resolution)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Resolution));
    }
}
