using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.WorkOrder;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.WorkOrder;

public class WorkOrderDtoValidatorTests
{
    private readonly WorkOrderDtoValidator _validator = new();

    private static WorkOrderDto Valid() => new()
    {
        OrderNo = "WO-2026-001",
        ProductId = 1,
        ClientId = 1,
        RecipeId = 1,
        PlannedQuantity = 100,
        Status = "Planned"
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("WO 001")]  // space
    [InlineData("WO#001")]  // special char
    public void OrderNo_non_alphanumeric_fails(string orderNo)
    {
        var dto = Valid();
        dto.OrderNo = orderNo;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.OrderNo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2_000_000)]
    public void PlannedQuantity_invalid_fails(int qty)
    {
        var dto = Valid();
        dto.PlannedQuantity = qty;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.PlannedQuantity);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("planned")] // case-sensitive 검증
    public void Status_not_in_enum_fails(string status)
    {
        var dto = Valid();
        dto.Status = status;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Start_after_end_fails()
    {
        var dto = Valid();
        dto.ActualStartAt = new DateTime(2026, 6, 2, 12, 0, 0);
        dto.ActualEndAt = new DateTime(2026, 6, 2, 11, 0, 0);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x);
    }
}
