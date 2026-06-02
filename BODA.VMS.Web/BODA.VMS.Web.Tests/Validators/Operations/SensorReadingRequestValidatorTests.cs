using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Operations;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Operations;

public class SensorReadingRequestValidatorTests
{
    private readonly SensorReadingRequestValidator _validator = new();

    [Fact]
    public void At_least_one_sensor_value_passes()
    {
        var dto = new SensorReadingRequest { ClientIndex = 0, TemperatureC = 25.0 };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void All_null_sensor_values_fails()
    {
        var dto = new SensorReadingRequest { ClientIndex = 0 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Theory]
    [InlineData(-100.0)]  // 너무 추움
    [InlineData(200.0)]   // 너무 뜨거움
    public void Temperature_out_of_physical_range_fails(double temp)
    {
        var dto = new SensorReadingRequest { ClientIndex = 0, TemperatureC = temp };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.TemperatureC);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(101.0)]
    public void Humidity_out_of_range_fails(double humidity)
    {
        var dto = new SensorReadingRequest { ClientIndex = 0, HumidityPct = humidity };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.HumidityPct);
    }
}
