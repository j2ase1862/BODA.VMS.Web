using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class ShiftDtoValidator : AbstractValidator<ShiftDto>
{
    public ShiftDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Shift name is required")
            .MaximumLength(50);

        RuleFor(x => x.StartHour)
            .InclusiveBetween(0, 23).WithMessage("StartHour must be 0-23");

        RuleFor(x => x.EndHour)
            .InclusiveBetween(0, 23).WithMessage("EndHour must be 0-23");

        // 같은 시각이면 0 시간 — 무의미한 교대 차단.
        RuleFor(x => x)
            .Must(s => s.StartHour != s.EndHour)
            .WithMessage("StartHour and EndHour must differ");
    }
}
