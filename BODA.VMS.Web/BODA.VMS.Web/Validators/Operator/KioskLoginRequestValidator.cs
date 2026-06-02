using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Operator;

public sealed class KioskLoginRequestValidator : AbstractValidator<KioskLoginRequest>
{
    public KioskLoginRequestValidator()
    {
        RuleFor(x => x.ClientIndex)
            .GreaterThanOrEqualTo(0).WithMessage("ClientIndex must be >= 0");

        RuleFor(x => x.EmployeeNumber)
            .NotEmpty().WithMessage("EmployeeNumber is required")
            .MaximumLength(50).WithMessage("EmployeeNumber max 50 characters");

        // PIN: 숫자 4-8자리 (산업 현장 키오스크 표준 길이)
        RuleFor(x => x.Pin)
            .NotEmpty().WithMessage("Pin is required")
            .Matches(@"^\d{4,8}$").WithMessage("Pin must be 4-8 digits");
    }
}
