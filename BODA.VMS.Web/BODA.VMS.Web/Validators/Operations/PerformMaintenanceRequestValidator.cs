using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Operations;

public sealed class PerformMaintenanceRequestValidator : AbstractValidator<PerformMaintenanceRequest>
{
    public PerformMaintenanceRequestValidator()
    {
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId.HasValue);

        RuleFor(x => x.ActualDurationMinutes)
            .GreaterThan(0)
            .LessThanOrEqualTo(1440)
            .When(x => x.ActualDurationMinutes.HasValue)
            .WithMessage("ActualDurationMinutes must be 1-1440 (24 hours)");

        RuleFor(x => x.Notes)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
