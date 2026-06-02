using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class MaintenanceScheduleUpsertDtoValidator : AbstractValidator<MaintenanceScheduleUpsertDto>
{
    public MaintenanceScheduleUpsertDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Maintenance schedule name is required")
            .MaximumLength(100);

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.IntervalDays)
            .GreaterThan(0).WithMessage("IntervalDays must be > 0")
            .LessThanOrEqualTo(3650).WithMessage("IntervalDays exceeds maximum (10 years)");

        RuleFor(x => x.EstimatedDurationMinutes)
            .GreaterThan(0).WithMessage("EstimatedDurationMinutes must be > 0")
            .LessThanOrEqualTo(1440).WithMessage("EstimatedDurationMinutes exceeds 24 hours");

        RuleFor(x => x.ClientId)
            .GreaterThan(0).When(x => x.ClientId.HasValue);
    }
}
