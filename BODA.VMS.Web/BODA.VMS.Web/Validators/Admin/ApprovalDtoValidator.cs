using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Admin;

public sealed class ApprovalDtoValidator : AbstractValidator<ApprovalDto>
{
    // 앱의 실제 역할 모델과 일치 (AdminApprovals 승인 버튼 3종 / 역할 칩 메뉴).
    // "Manager" 는 어디에도 없는 죽은 역할이었고 "Guest" 누락으로 Guest 승인이
    // 400 으로 거부되던 버그 수정 (2026-08-13)
    private static readonly string[] AllowedRoles = { "Admin", "User", "Guest" };

    public ApprovalDtoValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("UserId must be > 0");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(r => AllowedRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}");
    }
}
