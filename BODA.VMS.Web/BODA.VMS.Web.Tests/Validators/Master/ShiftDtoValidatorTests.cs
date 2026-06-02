using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class ShiftDtoValidatorTests
{
    private readonly ShiftDtoValidator _validator = new();

    [Fact]
    public void Day_shift_8_to_17_passes()
    {
        var dto = new ShiftDto { Name = "Day", StartHour = 8, EndHour = 17 };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Night_shift_22_to_6_passes()
    {
        // 자정 넘는 야간 교대도 허용 (StartHour > EndHour)
        var dto = new ShiftDto { Name = "Night", StartHour = 22, EndHour = 6 };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Same_start_and_end_fails()
    {
        var dto = new ShiftDto { Name = "Bad", StartHour = 9, EndHour = 9 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Hour_out_of_range_fails(int hour)
    {
        var dto = new ShiftDto { Name = "Test", StartHour = hour, EndHour = 17 };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.StartHour);
    }
}
