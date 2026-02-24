using System.Security.Claims;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
        {
            var result = await authService.LoginAsync(request);
            return result is null
                ? Results.Unauthorized()
                : Results.Ok(result);
        });

        group.MapPost("/register", async (RegisterRequest request, IAuthService authService) =>
        {
            var (success, message) = await authService.RegisterAsync(request);
            return success
                ? Results.Ok(new { message })
                : Results.BadRequest(new { message });
        });

        group.MapGet("/me", async (ClaimsPrincipal user, IAuthService authService) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var userDto = await authService.GetUserByIdAsync(userId);
            return userDto is null ? Results.NotFound() : Results.Ok(userDto);
        }).RequireAuthorization();
    }
}
