using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ClientService : IClientService
{
    private readonly BodaVmsDbContext _db;
    private readonly IConfiguration _configuration;

    public ClientService(BodaVmsDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<List<ClientDto>> GetAllClientsAsync()
    {
        var timeoutSeconds = _configuration.GetValue("ClientMonitor:HeartbeatTimeoutSeconds", 30);
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);

        var clients = await _db.Clients
            .OrderBy(c => c.ClientIndex)
            .ToListAsync();

        return clients.Select(c => ToDto(c, cutoff)).ToList();
    }

    public async Task<ClientDto?> GetClientByIdAsync(int id)
    {
        var c = await _db.Clients.FindAsync(id);
        if (c is null) return null;

        var timeoutSeconds = _configuration.GetValue("ClientMonitor:HeartbeatTimeoutSeconds", 30);
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);

        return ToDto(c, cutoff);
    }

    private static ClientDto ToDto(Data.Entities.VisionClient c, DateTime cutoff)
    {
        return new ClientDto
        {
            Id = c.Id,
            Name = c.Name,
            IpAddress = c.IpAddress,
            ClientIndex = c.ClientIndex,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            LastSeenAt = c.LastSeenAt,
            LastHeartbeatIp = c.LastHeartbeatIp,
            HostName = c.HostName,
            SwName = c.SwName,
            IsAlive = c.LastSeenAt.HasValue && c.LastSeenAt.Value > cutoff
        };
    }

    public async Task<ClientDto> CreateClientAsync(ClientDto dto)
    {
        var entity = new Data.Entities.VisionClient
        {
            Name = dto.Name,
            IpAddress = dto.IpAddress,
            ClientIndex = dto.ClientIndex,
            IsActive = dto.IsActive
        };

        _db.Clients.Add(entity);
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task<ClientDto?> UpdateClientAsync(int id, ClientDto dto)
    {
        var entity = await _db.Clients.FindAsync(id);
        if (entity is null) return null;

        entity.Name = dto.Name;
        entity.IpAddress = dto.IpAddress;
        entity.ClientIndex = dto.ClientIndex;
        entity.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        dto.LastSeenAt = entity.LastSeenAt;
        return dto;
    }

    public async Task<bool> DeleteClientAsync(int id)
    {
        var entity = await _db.Clients.FindAsync(id);
        if (entity is null) return false;

        _db.Clients.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<RecipeDto>> GetRecipesByClientIdAsync(int clientId)
    {
        return await _db.Recipes
            .Where(r => r.ClientId == clientId)
            .Include(r => r.Steps)
                .ThenInclude(s => s.InspectionTools)
            .Select(r => new RecipeDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ClientId = r.ClientId,
                CreatedAt = r.CreatedAt,
                Steps = r.Steps.OrderBy(s => s.OrderIndex).Select(s => new StepDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    OrderIndex = s.OrderIndex,
                    InspectionTools = s.InspectionTools.OrderBy(t => t.OrderIndex).Select(t => new InspectionToolDto
                    {
                        Id = t.Id,
                        Name = t.Name,
                        ToolType = t.ToolType,
                        OrderIndex = t.OrderIndex
                    }).ToList()
                }).ToList()
            })
            .ToListAsync();
    }
}
