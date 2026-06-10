using FluentValidation;

namespace BODA.VMS.Web.Validators;

/// <summary>
/// KISA 패스워드 가이드라인 기준 복잡도 정책 (회원가입/비밀번호 재설정 공용):
/// 문자종류(대문자/소문자/숫자/특수문자) 3종 이상 조합이면 8자 이상,
/// 2종 조합이면 10자 이상.
/// </summary>
public static class PasswordPolicy
{
    public const string ComplexityMessage =
        "Password must combine 3+ character types (upper/lower/digit/special) with 8+ chars, " +
        "or 2+ types with 10+ chars";

    public static IRuleBuilderOptions<T, string> MustSatisfyPasswordComplexity<T>(
        this IRuleBuilder<T, string> rule) =>
        rule.Must(IsComplexEnough).WithMessage(ComplexityMessage);

    public static bool IsComplexEnough(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        var classes = 0;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        return (classes >= 3 && password.Length >= 8)
            || (classes >= 2 && password.Length >= 10);
    }
}
