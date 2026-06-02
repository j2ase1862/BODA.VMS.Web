using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.WorkOrder;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.WorkOrder;

public class WorkOrderStatusChangeRequestValidatorTests
{
    private readonly WorkOrderStatusChangeRequestValidator _validator = new();

    [Theory]
    [InlineData("Start")]
    [InlineData("Complete")]
    [InlineData("Close")]
    public void Allowed_actions_pass(string action)
    {
        _validator.TestValidate(new WorkOrderStatusChangeRequest { Action = action })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("start")]  // case-sensitive
    [InlineData("Unknown")]
    public void Invalid_action_fails(string action)
    {
        _validator.TestValidate(new WorkOrderStatusChangeRequest { Action = action })
            .ShouldHaveValidationErrorFor(x => x.Action);
    }
}
