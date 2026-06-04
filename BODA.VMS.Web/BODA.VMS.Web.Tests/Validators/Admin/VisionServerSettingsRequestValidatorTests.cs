using BODA.VMS.Web.Endpoints;
using BODA.VMS.Web.Validators.Admin;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Admin;

public class VisionServerSettingsRequestValidatorTests
{
    private readonly VisionServerSettingsRequestValidator _validator = new();

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://vision.example.com")]
    [InlineData("http://10.0.0.5:8080/api")]
    public void Valid_absolute_http_url_passes(string baseUrl)
    {
        _validator.TestValidate(new VisionServerSettingsRequest(true, baseUrl))
            .ShouldNotHaveValidationErrorFor(x => x.BaseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_baseurl_passes(string? baseUrl)
    {
        // 비활성화(Enabled=false) 시나리오 — BaseUrl 미지정 허용
        _validator.TestValidate(new VisionServerSettingsRequest(false, baseUrl))
            .ShouldNotHaveValidationErrorFor(x => x.BaseUrl);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("vision.example.com")]                 // 스키마 없음
    [InlineData("/relative/path")]                     // 상대 경로
    [InlineData("ftp://server/path")]                  // 비-http 스키마
    [InlineData("file:///C:/secret.txt")]              // 파일 스키마 차단
    [InlineData("javascript:alert(1)")]                // XSS 우회 차단
    public void Invalid_baseurl_fails(string baseUrl)
    {
        _validator.TestValidate(new VisionServerSettingsRequest(true, baseUrl))
            .ShouldHaveValidationErrorFor(x => x.BaseUrl);
    }

    [Fact]
    public void Baseurl_exceeding_max_length_fails()
    {
        var longTail = new string('x', 500);
        var url = $"http://example.com/{longTail}";
        _validator.TestValidate(new VisionServerSettingsRequest(true, url))
            .ShouldHaveValidationErrorFor(x => x.BaseUrl);
    }
}
