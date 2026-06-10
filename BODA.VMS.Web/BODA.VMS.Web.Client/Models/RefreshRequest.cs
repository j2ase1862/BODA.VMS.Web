using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Client.Models;

/// <summary>access token 갱신(/api/auth/refresh) 및 로그아웃(/api/auth/logout) 요청 본문.</summary>
public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
