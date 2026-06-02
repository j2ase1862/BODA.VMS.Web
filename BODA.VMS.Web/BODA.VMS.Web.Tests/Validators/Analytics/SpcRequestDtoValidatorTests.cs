using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Analytics;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Analytics;

public class SpcRequestDtoValidatorTests
{
    private readonly SpcRequestDtoValidator _validator = new();

    private static SpcRequestDto Valid() => new() { RecipeId = 1, ParamCode = 1, SubgroupSize = 5 };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(1)]   // 너무 작음
    [InlineData(11)]  // 너무 큼
    public void SubgroupSize_out_of_range_fails(int size)
    {
        var dto = Valid();
        dto.SubgroupSize = size;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.SubgroupSize);
    }

    [Fact]
    public void Start_after_end_fails()
    {
        var dto = Valid();
        dto.StartDate = new DateTime(2026, 6, 2);
        dto.EndDate = new DateTime(2026, 6, 1);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Only_start_or_only_end_passes()
    {
        // 한쪽만 지정시 정합성 검증 스킵 (nullable)
        var dto = Valid();
        dto.StartDate = new DateTime(2026, 6, 1);
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }
}
