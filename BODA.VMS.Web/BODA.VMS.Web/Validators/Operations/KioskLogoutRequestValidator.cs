using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Operations;

public sealed class KioskLogoutRequestValidator : AbstractValidator<KioskLogoutRequest>
{
    public KioskLogoutRequestValidator()
    {
        RuleFor(x => x.ClientIndex).GreaterThanOrEqualTo(0);
    }
}
