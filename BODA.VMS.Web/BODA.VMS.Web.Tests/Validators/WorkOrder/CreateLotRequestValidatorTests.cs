using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.WorkOrder;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.WorkOrder;

public class CreateLotRequestValidatorTests
{
    private readonly CreateLotRequestValidator _validator = new();

    [Fact]
    public void Null_note_passes()
    {
        _validator.TestValidate(new CreateLotRequest { Note = null })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Short_note_passes()
    {
        _validator.TestValidate(new CreateLotRequest { Note = "긴급 로트" })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Note_over_500_chars_fails()
    {
        _validator.TestValidate(new CreateLotRequest { Note = new string('가', 501) })
            .ShouldHaveValidationErrorFor(x => x.Note);
    }
}
