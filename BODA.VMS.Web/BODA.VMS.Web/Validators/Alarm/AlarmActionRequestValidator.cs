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

        // Resolve 시 Resolution 필수, Acknowledge 시 선택적.
        RuleFor(x => x.Resolution)
            .NotEmpty()
            .When(x => x.Action == "Resolve")
            .WithMessage("Resolution is required when Action is 'Resolve'")
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Resolution));
    }
}
