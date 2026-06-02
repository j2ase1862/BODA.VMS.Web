using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Inspection;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Inspection;

public class ParameterResultUploadRequestValidatorTests
{
    private readonly ParameterResultUploadRequestValidator _validator = new();

    private static ParameterResultUploadRequest Valid() => new()
    {
        ClientIndex = 0,
        RecipeId = 1,
        Results = new List<ParameterResultDto>
        {
            new() { ParamCode = 1, MeasuredValue = 1.0, Judgment = "Pass", Timestamp = DateTime.UtcNow }
        }
    };

    [Fact]
    public void Valid_minimal_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_results_fails()
    {
        var dto = Valid();
        dto.Results = new List<ParameterResultDto>();
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Results);
    }

    [Fact]
    public void Over_1000_results_fails()
    {
        var dto = Valid();
        dto.Results = Enumerable.Range(1, 1001)
            .Select(i => new ParameterResultDto { ParamCode = i, Judgment = "Pass" })
            .ToList();
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Results);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(256.0)]
    public void Brightness_out_of_range_fails(double brightness)
    {
        var dto = Valid();
        dto.Brightness = brightness;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Brightness);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void DlConfidence_out_of_range_fails(double confidence)
    {
        var dto = Valid();
        dto.DlConfidence = confidence;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.DlConfidence);
    }

    [Fact]
    public void Result_item_with_invalid_param_code_fails()
    {
        var dto = Valid();
        dto.Results[0].ParamCode = 0; // 잘못된 ParamCode
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("Results[0].ParamCode");
    }
}
