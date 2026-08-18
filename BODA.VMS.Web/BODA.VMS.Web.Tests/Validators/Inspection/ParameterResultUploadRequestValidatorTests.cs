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

    [Fact]
    public void Empty_results_with_overall_pass_passes()
    {
        // 판정 전용(사이클) 업로드 — AUTO RUN "1사이클 = 1개" 집계 (VMS v1.5.18+).
        var dto = Valid();
        dto.Results = new List<ParameterResultDto>();
        dto.OverallPass = true;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_results_with_overall_fail_passes()
    {
        var dto = Valid();
        dto.Results = new List<ParameterResultDto>();
        dto.OverallPass = false;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Overall_pass_with_over_1000_results_still_fails()
    {
        // OverallPass 가 있어도 상한은 그대로 적용된다.
        var dto = Valid();
        dto.OverallPass = true;
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
