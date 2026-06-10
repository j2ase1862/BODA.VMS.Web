using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Auth;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Auth;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Valid() => new()
    {
        Username = "alice_2026",
        Password = "P@ssw0rd1!",
        DisplayName = "Alice"
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("alice user")]   // space
    [InlineData("alice!")]       // special char
    [InlineData("한국어")]        // non-ASCII
    public void Username_non_alphanumeric_fails(string username)
    {
        var dto = Valid();
        dto.Username = username;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Password_less_than_8_chars_fails()
    {
        var dto = Valid();
        dto.Password = "abc1234"; // 7 chars
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must be at least 8 characters");
    }

    // KISA 복잡도: 3종 조합이면 8자+, 2종 조합이면 10자+
    [Theory]
    [InlineData("Abcdef1!")]      // 8자, 4종 (대/소/숫/특)
    [InlineData("abcdefgh12")]    // 10자, 2종 (소/숫)
    [InlineData("Abcdefg1")]      // 8자, 3종 (대/소/숫)
    public void Password_meeting_complexity_passes(string password)
    {
        var dto = Valid();
        dto.Password = password;
        _validator.TestValidate(dto).ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("abcdefg1")]      // 8자, 2종 — 10자 미만이라 부족
    [InlineData("abcdefghij")]    // 10자, 1종 (소문자만)
    [InlineData("12345678901")]   // 11자, 1종 (숫자만)
    [InlineData("secret12")]      // 8자, 2종 — 과거 약한 패턴 차단 확인
    public void Password_failing_complexity_fails(string password)
    {
        var dto = Valid();
        dto.Password = password;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void DisplayName_required()
    {
        var dto = Valid();
        dto.DisplayName = "";
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.DisplayName);
    }
}
