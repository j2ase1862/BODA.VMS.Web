using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.WorkOrder;

public sealed class CreateLotRequestValidator : AbstractValidator<CreateLotRequest>
{
    public CreateLotRequestValidator()
    {
        // Note 는 nullable 이므로 값이 있을 때만 길이 검증.
        RuleFor(x => x.Note)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Note));
    }
}
