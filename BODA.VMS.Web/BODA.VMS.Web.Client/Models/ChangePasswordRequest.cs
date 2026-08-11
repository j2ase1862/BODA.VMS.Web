namespace BODA.VMS.Web.Client.Models;

/// <summary>본인 비밀번호 변경 요청 (/api/auth/change-password, 2026-08-11).</summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
