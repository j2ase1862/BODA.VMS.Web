using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Operations;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Operations;

public class KioskLogoutRequestValidatorTests
{
    private readonly KioskLogoutRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(new KioskLogoutRequest { ClientIndex = 0 })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Negative_client_index_fails()
    {
        _validator.TestValidate(new KioskLogoutRequest { ClientIndex = -1 })
            .ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }
}
