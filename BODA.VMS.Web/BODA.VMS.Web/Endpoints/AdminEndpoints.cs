using BODA.VMS.Web.Client.Models;
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
        });

        group.MapPost("/reject/{userId:int}", async (int userId, IUserManagementService userService) =>
        {
            var success = await userService.RejectUserAsync(userId);
            return success ? Results.Ok() : Results.NotFound();
        });

        group.MapPost("/reset-password", async (ResetPasswordRequest request, IUserManagementService userService) =>
        {
            var success = await userService.ResetPasswordAsync(request.UserId, request.NewPassword);
            return success ? Results.Ok() : Results.NotFound();
        });
    }
}

public class ResetPasswordRequest
{
    public int UserId { get; set; }
    public string NewPassword { get; set; } = string.Empty;
}
