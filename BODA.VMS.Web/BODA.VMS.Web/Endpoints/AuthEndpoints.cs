using System.Security.Claims;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
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
        }).AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()
          .RequireRateLimiting(LoginRateLimitOptions.PolicyName); // GS 보안 — brute-force 방어

        // GS 보안 — access token 갱신 (refresh token 로테이션). brute-force 방어 위해 rate limit.
        group.MapPost("/refresh", async (RefreshRequest request, IAuthService authService) =>
        {
            var result = await authService.RefreshAsync(request.RefreshToken);
            return result is null
                ? Results.Unauthorized()
                : Results.Ok(result);
        }).RequireRateLimiting(LoginRateLimitOptions.PolicyName);

        // GS 보안 — 로그아웃: refresh token 폐기 (서버측 revocation). 토큰 만료돼도 호출 가능하도록 익명.
        group.MapPost("/logout", async (RefreshRequest request, IAuthService authService) =>
        {
            await authService.RevokeRefreshTokenAsync(request.RefreshToken);
            return Results.Ok();
        });

        group.MapPost("/register", async (RegisterRequest request, IAuthService authService) =>
        {
            var (success, message) = await authService.RegisterAsync(request);
            return success
                ? Results.Ok(new { message })
                : Results.BadRequest(new { message });
        }).AddEndpointFilter<ValidationEndpointFilter<RegisterRequest>>();

        // 본인 비밀번호 변경 (2026-08-11) — 임시 비밀번호 강제 변경 흐름 포함, 모든 역할 허용.
        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal user,
            IAuthService authService) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var (success, error) = await authService.ChangePasswordAsync(
                userId, request.CurrentPassword, request.NewPassword);
            return success
                ? Results.Ok()
                : Results.BadRequest(new { message = error });
        }).AddEndpointFilter<ValidationEndpointFilter<ChangePasswordRequest>>()
          .RequireAuthorization();

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
