using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Analytics;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Analytics;

// Reliability / Oee / ShiftReport / Report 4 개 Validator 의 공통 패턴
// (StartDate≤EndDate, Optional ClientId>0) 을 모아 검증.

public class ReliabilityRequestDtoValidatorTests
{
    private readonly ReliabilityRequestDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new ReliabilityRequestDto
        {
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 2)
        };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Start_after_end_fails()
    {
        var dto = new ReliabilityRequestDto
        {
            StartDate = new DateTime(2026, 6, 2),
            EndDate = new DateTime(2026, 6, 1)
        };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Default_start_date_fails()
    {
        var dto = new ReliabilityRequestDto { EndDate = new DateTime(2026, 6, 2) };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.StartDate);
    }

    [Fact]
    public void Zero_client_id_fails()
    {
        var dto = new ReliabilityRequestDto
        {
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 2),
            ClientId = 0
        };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.ClientId);
    }
}

public class OeeRequestDtoValidatorTests
{
    private readonly OeeRequestDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new OeeRequestDto
        {
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 2)
        };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Start_after_end_fails()
    {
        var dto = new OeeRequestDto
        {
            StartDate = new DateTime(2026, 6, 2),
            EndDate = new DateTime(2026, 6, 1)
        };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x);
    }
}

public class ShiftReportRequestDtoValidatorTests
{
    private readonly ShiftReportRequestDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new ShiftReportRequestDto
        {
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 2)
        };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void End_before_start_fails()
    {
        var dto = new ShiftReportRequestDto
        {
            StartDate = new DateTime(2026, 6, 2),
            EndDate = new DateTime(2026, 6, 1)
        };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x);
    }
}

public class ReportRequestDtoValidatorTests
{
    private readonly ReportRequestDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new ReportRequestDto
        {
            Type = ReportType.Daily,
            ReferenceDate = new DateTime(2026, 6, 2)
        };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Default_reference_date_fails()
    {
        var dto = new ReportRequestDto { Type = ReportType.Daily };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.ReferenceDate);
    }

    [Fact]
    public void Invalid_enum_value_fails()
    {
        var dto = new ReportRequestDto
        {
            Type = (ReportType)999,  // 정의되지 않은 enum
            ReferenceDate = new DateTime(2026, 6, 2)
        };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Type);
    }
}
