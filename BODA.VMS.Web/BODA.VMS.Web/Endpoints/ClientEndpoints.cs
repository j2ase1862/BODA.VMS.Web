using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        // Anonymous heartbeat endpoint — vision clients don't authenticate
        app.MapPost("/api/clients/heartbeat", async (
            HeartbeatRequest request,
            BodaVmsDbContext db,
            HttpContext httpContext,
            ILogger<Program> logger) =>
        {
            var client = await db.Clients
                .FirstOrDefaultAsync(c => c.ClientIndex == request.ClientIndex);

            if (client is null)
                return Results.NotFound();

            var remoteIp = httpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";

            // Detect duplicate: different source IP for the same ClientIndex
            if (client.LastHeartbeatIp is not null
                && client.LastHeartbeatIp != remoteIp
                && client.LastSeenAt.HasValue
                && (DateTime.UtcNow - client.LastSeenAt.Value).TotalSeconds < 30)
            {
                logger.LogWarning(
                    "ClientIndex {Index} heartbeat from {NewIp} while previous source {OldIp} is still active",
                    request.ClientIndex, remoteIp, client.LastHeartbeatIp);
            }

            client.LastSeenAt = DateTime.UtcNow;
            client.LastHeartbeatIp = remoteIp;
            client.HostName = request.HostName;
            client.SwName = request.SwName;
            await db.SaveChangesAsync();

            return Results.Ok();
        }).AllowAnonymous();

        // Vision client sends this on graceful shutdown → immediate offline
        app.MapPost("/api/clients/disconnect", async (
            DisconnectRequest request,
            BodaVmsDbContext db,
            IHubContext<VmsHub> hubContext) =>
        {
            var client = await db.Clients
                .FirstOrDefaultAsync(c => c.ClientIndex == request.ClientIndex);

            if (client is null)
                return Results.NotFound();

            client.LastSeenAt = null;
            await db.SaveChangesAsync();

            await hubContext.Clients.All.SendAsync("ClientStatusChanged", client.Id, false);

            return Results.Ok();
        }).AllowAnonymous();

        var group = app.MapGroup("/api/clients")
            .RequireAuthorization();

        group.MapGet("/", async (IClientService clientService) =>
        {
            var clients = await clientService.GetAllClientsAsync();
            return Results.Ok(clients);
        });

        group.MapGet("/{id:int}", async (int id, IClientService clientService) =>
        {
            var client = await clientService.GetClientByIdAsync(id);
            return client is null ? Results.NotFound() : Results.Ok(client);
        });

        group.MapPost("/", async (ClientDto dto, IClientService clientService) =>
        {
            var result = await clientService.CreateClientAsync(dto);
            return Results.Created($"/api/clients/{result.Id}", result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapPut("/{id:int}", async (int id, ClientDto dto, IClientService clientService) =>
        {
            var result = await clientService.UpdateClientAsync(id, dto);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapDelete("/{id:int}", async (int id, IClientService clientService) =>
        {
            var success = await clientService.DeleteClientAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/{id:int}/recipes", async (int id, IClientService clientService) =>
        {
            var recipes = await clientService.GetRecipesByClientIdAsync(id);
            return Results.Ok(recipes);
        });
    }
}

public record HeartbeatRequest(int ClientIndex, string? HostName = null, string? SwName = null);

public record DisconnectRequest(int ClientIndex);
