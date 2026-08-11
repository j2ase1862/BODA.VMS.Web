using System.Security.Claims;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/pending-users", async (IUserManagementService userService) =>
        {
            var users = await userService.GetPendingUsersAsync();
            return Results.Ok(users);
        });

        group.MapGet("/users", async (IUserManagementService userService) =>
        {
            var users = await userService.GetAllUsersAsync();
            return Results.Ok(users);
        });

        group.MapPost("/approve", async (ApprovalDto dto, IUserManagementService userService) =>
        {
            var success = await userService.ApproveUserAsync(dto.UserId, dto.Role);
            return success ? Results.Ok() : Results.NotFound();
        }).AddEndpointFilter<ValidationEndpointFilter<ApprovalDto>>();

        group.MapPost("/reject/{userId:int}", async (int userId, IUserManagementService userService) =>
        {
            var success = await userService.RejectUserAsync(userId);
            return success ? Results.Ok() : Results.NotFound();
        });

        // 비밀번호 초기화 (2026-08-11 재설계) — 서버가 임시 비밀번호 생성,
        // 평문은 이 응답으로 1회만 노출. 대상 사용자는 다음 로그인 시 변경 강제.
        group.MapPost("/users/{userId:int}/reset-password", async (
            int userId,
            IUserManagementService userService) =>
        {
            var (success, tempPassword) = await userService.ResetPasswordAsync(userId);
            return success ? Results.Ok(new { tempPassword }) : Results.NotFound();
        });

        // 사용자 삭제 (2026-08-11) — 자기 자신·최초 admin·마지막 Admin 은 409 로 차단
        group.MapDelete("/users/{userId:int}", async (
            int userId,
            ClaimsPrincipal principal,
            IUserManagementService userService) =>
        {
            var actingIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (actingIdClaim is null || !int.TryParse(actingIdClaim, out var actingId))
                return Results.Unauthorized();

            var (success, error) = await userService.DeleteUserAsync(userId, actingId);
            if (success) return Results.Ok();
            return error == "User not found"
                ? Results.NotFound()
                : Results.Conflict(new { message = error });
        });

        // 승인된 사용자 역할 변경 (2026-08-11) — 마지막 Admin 강등은 서비스가 차단(409)
        group.MapPut("/users/{userId:int}/role", async (
            int userId,
            ChangeRoleRequest request,
            IUserManagementService userService) =>
        {
            if (request.Role is not ("Admin" or "User" or "Guest"))
                return Results.BadRequest(new { message = "Role must be Admin, User, or Guest" });

            var (success, error) = await userService.ChangeRoleAsync(userId, request.Role);
            if (success) return Results.Ok();
            return error == "User not found"
                ? Results.NotFound()
                : Results.Conflict(new { message = error });
        });
    }
}

public class ChangeRoleRequest
{
    public string Role { get; set; } = string.Empty;
}
