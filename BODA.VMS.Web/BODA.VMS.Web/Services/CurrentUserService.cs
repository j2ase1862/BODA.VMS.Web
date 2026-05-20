using System.Security.Claims;

namespace BODA.VMS.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public int? UserId
    {
        get
        {
            var s = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(s, out var id) ? id : null;
        }
    }

    public string? UserName =>
        _accessor.HttpContext?.User.FindFirstValue("DisplayName")
        ?? _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string? IpAddress =>
        _accessor.HttpContext?.Connection.RemoteIpAddress?.MapToIPv4().ToString();
}
