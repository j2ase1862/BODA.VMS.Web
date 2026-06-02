using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class MaintenanceScheduleUpsertDtoValidatorTests
{
    private readonly MaintenanceScheduleUpsertDtoValidator _validator = new();

    private static MaintenanceScheduleUpsertDto Valid() => new()
    {
        Name = "Daily Camera Lens Clean",
        IntervalDays = 1,
        EstimatedDurationMinutes = 10
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]  // > 10 years
    public void IntervalDays_out_of_range_fails(int days)
    {
        var dto = Valid();
        dto.IntervalDays = days;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.IntervalDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]  // > 24h
    public void Duration_out_of_range_fails(int minutes)
    {
        var dto = Valid();
        dto.EstimatedDurationMinutes = minutes;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.EstimatedDurationMinutes);
    }

    [Fact]
    public void Empty_name_fails()
    {
        var dto = Valid();
        dto.Name = "";
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Name);
    }
}
