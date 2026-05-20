namespace BODA.VMS.Web.Services;

/// <summary>HttpContext에서 현재 인증된 사용자 정보를 추출. 백그라운드 컨텍스트면 모두 null.</summary>
public interface ICurrentUserService
{
    int? UserId { get; }
    string? UserName { get; }
    string? IpAddress { get; }
}
