using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.WorkOrder;

public sealed class WorkOrderStatusChangeRequestValidator : AbstractValidator<WorkOrderStatusChangeRequest>
{
    private static readonly string[] AllowedActions = { "Start", "Complete", "Close" };

    public WorkOrderStatusChangeRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty().WithMessage("Action is required")
            .Must(a => AllowedActions.Contains(a))
            .WithMessage($"Action must be one of: {string.Join(", ", AllowedActions)}");
    }
}
