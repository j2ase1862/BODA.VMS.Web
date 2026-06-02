using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Operations;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Operations;

public class PerformMaintenanceRequestValidatorTests
{
    private readonly PerformMaintenanceRequestValidator _validator = new();

    [Fact]
    public void Empty_request_passes()
    {
        // 모든 필드 nullable — 빈 요청도 허용
        _validator.TestValidate(new PerformMaintenanceRequest())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]  // > 24h
    public void Duration_out_of_range_fails(int minutes)
    {
        _validator.TestValidate(new PerformMaintenanceRequest { ActualDurationMinutes = minutes })
            .ShouldHaveValidationErrorFor(x => x.ActualDurationMinutes);
    }

    [Fact]
    public void Notes_over_500_fails()
    {
        _validator.TestValidate(new PerformMaintenanceRequest { Notes = new string('가', 501) })
            .ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Zero_client_id_fails()
    {
        _validator.TestValidate(new PerformMaintenanceRequest { ClientId = 0 })
            .ShouldHaveValidationErrorFor(x => x.ClientId);
    }
}
