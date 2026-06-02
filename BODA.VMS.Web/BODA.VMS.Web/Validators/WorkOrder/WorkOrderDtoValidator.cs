using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.WorkOrder;

public sealed class WorkOrderDtoValidator : AbstractValidator<WorkOrderDto>
{
    private static readonly string[] AllowedStatuses =
        { "Planned", "InProgress", "Completed", "Closed", "Cancelled" };

    public WorkOrderDtoValidator()
    {
        RuleFor(x => x.OrderNo)
            .NotEmpty().WithMessage("OrderNo is required")
            .MaximumLength(50).WithMessage("OrderNo max 50 characters")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("OrderNo must be alphanumeric (_- allowed)");

        RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("ProductId must be > 0");
        RuleFor(x => x.ClientId).GreaterThan(0).WithMessage("ClientId must be > 0");
        RuleFor(x => x.RecipeId).GreaterThan(0).WithMessage("RecipeId must be > 0");

        RuleFor(x => x.PlannedQuantity)
            .GreaterThan(0).WithMessage("PlannedQuantity must be > 0")
            .LessThanOrEqualTo(1_000_000).WithMessage("PlannedQuantity exceeds maximum");

        RuleFor(x => x.Status)
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}");

        RuleFor(x => x.Note)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Note));

        // 시작 ≤ 종료 (둘 다 지정된 경우)
        RuleFor(x => x)
            .Must(w => !(w.ActualStartAt.HasValue && w.ActualEndAt.HasValue)
                      || w.ActualStartAt <= w.ActualEndAt)
            .WithMessage("ActualStartAt must be <= ActualEndAt");
    }
}
