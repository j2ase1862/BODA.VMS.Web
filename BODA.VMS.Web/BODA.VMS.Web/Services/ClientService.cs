using System.Text;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ClientService : IClientService
{
    private readonly BodaVmsDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClientService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ClientService(
        BodaVmsDbContext db,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ClientService> logger)
    {
        _db = db;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
        // 1. VisionServer에 Client 생성 (shared DB에 직접 INSERT됨)
        var visionServerResult = await CreateClientOnVisionServerAsync(dto);

        // 2. VisionServer가 shared DB에 이미 INSERT한 행을 읽어서 Web 전용 컬럼 업데이트
        // DbContext 캐시를 우회하여 DB에서 직접 읽기
        var entity = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == visionServerResult.Id);

        if (entity is null)
        {
            // VisionServer가 아직 커밋하지 않았거나 다른 DB를 쓰는 경우 fallback
            entity = new Data.Entities.VisionClient
            {
                Id = visionServerResult.Id,
                Name = visionServerResult.Name,
                IpAddress = visionServerResult.IpAddress,
                ClientIndex = visionServerResult.Index,
            };
            _db.Clients.Add(entity);
        }

        entity.IsActive = dto.IsActive;
        entity.CreatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.ClientIndex = entity.ClientIndex;
        dto.CreatedAt = entity.CreatedAt;

        _logger.LogInformation(
            "Client '{Name}' created (VisionServer Id={Id}, Index={Index})",
            dto.Name, dto.Id, dto.ClientIndex);

        return dto;
    }

    public async Task<ClientDto?> UpdateClientAsync(int id, ClientDto dto)
    {
        var entity = await _db.Clients.FindAsync(id);
        if (entity is null) return null;

        // 1. VisionServer에 먼저 동기화
        await UpdateClientOnVisionServerAsync(id, dto);

        // 2. Web DB 업데이트
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

        // 1. VisionServer에서 먼저 삭제
        await DeleteClientFromVisionServerAsync(id);

        // 2. Web DB에서 삭제
        _db.Clients.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<RecipeDto>> GetRecipesByClientIdAsync(int clientId)
    {
        // Note: VisionServer의 DB 구조는 Recipe → Camera → Step → InspectionTool 이지만,
        // Web에서는 레시피 목록만 필요하므로 하위 계층 Include를 하지 않습니다.
        // 하위 계층(Camera/Step/Tool)은 VisionSetup이 VisionServer API를 통해 직접 관리합니다.
        return await _db.Recipes
            .Where(r => r.ClientId == clientId)
            .OrderBy(r => r.RecipeIndex)
            .Select(r => new RecipeDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ClientId = r.ClientId,
                CreatedAt = r.CreatedAt ?? DateTime.MinValue
            })
            .ToListAsync();
    }


    #region VisionServer API (Single Source of Truth)

    private string GetVisionServerBaseUrl()
    {
        var baseUrl = _configuration["VisionServer:BaseUrl"];
        if (string.IsNullOrEmpty(baseUrl))
            throw new InvalidOperationException(
                "VisionServer:BaseUrl is not configured. VisionServer is the single data owner and must be reachable.");
        return baseUrl.TrimEnd('/');
    }

    private HttpClient CreateVisionServerClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    /// <summary>
    /// VisionServer에 Client를 생성하고 부여된 Id를 포함한 결과를 반환합니다.
    /// VisionServer가 단일 데이터 소유자이므로, 실패 시 예외가 발생합니다.
    /// </summary>
    private async Task<VisionServerClientDto> CreateClientOnVisionServerAsync(ClientDto dto)
    {
        var baseUrl = GetVisionServerBaseUrl();

        var payload = new
        {
            Name = dto.Name,
            IpAddress = dto.IpAddress,
            Index = dto.ClientIndex
        };

        var httpClient = CreateVisionServerClient();
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{baseUrl}/api/v1/Clients", content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"VisionServer Client creation failed: {response.StatusCode} - {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<VisionServerClientDto>(responseBody, _jsonOptions);

        if (result == null || result.Id <= 0)
            throw new InvalidOperationException(
                "VisionServer returned invalid Client data.");

        return result;
    }

    /// <summary>
    /// VisionServer의 Client 정보를 업데이트합니다.
    /// VisionServer의 PostClient는 동일 Index가 있으면 업데이트합니다.
    /// </summary>
    private async Task UpdateClientOnVisionServerAsync(int id, ClientDto dto)
    {
        try
        {
            var baseUrl = GetVisionServerBaseUrl();

            var payload = new
            {
                Id = id,
                Name = dto.Name,
                IpAddress = dto.IpAddress,
                Index = dto.ClientIndex
            };

            var httpClient = CreateVisionServerClient();
            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{baseUrl}/api/v1/Clients", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "VisionServer Client update failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update client on VisionServer.");
        }
    }

    private async Task DeleteClientFromVisionServerAsync(int clientId)
    {
        try
        {
            var baseUrl = GetVisionServerBaseUrl();
            var httpClient = CreateVisionServerClient();
            await httpClient.DeleteAsync($"{baseUrl}/api/v1/Clients/{clientId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete client from VisionServer.");
        }
    }

    #endregion

    #region Recipe CRUD (Web이 ID 발급 — Single Source of Truth)

    public async Task<RecipeDto> CreateRecipeAsync(int clientId, RecipeDto dto)
    {
        // 1. VisionServer에 Recipe 생성 (shared DB에 INSERT)
        var visionServerResult = await CreateRecipeOnVisionServerAsync(clientId, dto);

        // 2. VisionServer가 shared DB에 INSERT한 행을 읽어 확인
        var entity = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == visionServerResult.RecipeId);

        if (entity is null)
        {
            entity = new Data.Entities.Recipe
            {
                Id = visionServerResult.RecipeId,
                Name = visionServerResult.RecipeName,
                Description = dto.Description,
                ClientId = clientId,
                CreatedAt = DateTime.UtcNow
            };
            _db.Recipes.Add(entity);
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Recipe '{Name}' created (Id={Id}) for Client {ClientId}",
            entity.Name, entity.Id, clientId);

        return new RecipeDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            ClientId = entity.ClientId,
            CreatedAt = entity.CreatedAt ?? DateTime.MinValue
        };
    }

    public async Task<bool> DeleteRecipeAsync(int recipeId)
    {
        // 1. VisionServer에서 삭제
        await DeleteRecipeFromVisionServerAsync(recipeId);

        // 2. Web DB에서 관련 파라미터 삭제
        var parameters = await _db.RecipeParameters
            .Where(p => p.RecipeId == recipeId)
            .ToListAsync();
        _db.RecipeParameters.RemoveRange(parameters);

        // 3. Recipe 삭제
        var entity = await _db.Recipes.FindAsync(recipeId);
        if (entity is null) return false;
        _db.Recipes.Remove(entity);

        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<VisionServerRecipeDto> CreateRecipeOnVisionServerAsync(int clientId, RecipeDto dto)
    {
        var baseUrl = GetVisionServerBaseUrl();

        // VisionServer의 ShareLibrary.DTOs.RecipeDto 필드명에 맞춤
        var payload = new
        {
            RecipeName = dto.Name,      // VisionServer는 "RecipeName" 필드를 기대
            ClientId = clientId,
            RecipeIndex = 0             // 0이면 VisionServer가 자동 채번
        };

        var httpClient = CreateVisionServerClient();
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{baseUrl}/api/v1/Recipes", content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"VisionServer Recipe creation failed: {response.StatusCode} - {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<VisionServerRecipeDto>(responseBody, _jsonOptions);

        if (result == null || result.RecipeId <= 0)
            throw new InvalidOperationException("VisionServer returned invalid Recipe data.");

        return result;
    }

    private async Task DeleteRecipeFromVisionServerAsync(int recipeId)
    {
        try
        {
            var baseUrl = GetVisionServerBaseUrl();
            var httpClient = CreateVisionServerClient();
            await httpClient.DeleteAsync($"{baseUrl}/api/v1/Recipes/{recipeId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete recipe from VisionServer.");
        }
    }

    #endregion

    /// <summary>
    /// VisionServer API 응답용 DTO (ShareLibrary.DTOs.ClientDto와 동일 구조)
    /// </summary>
    private class VisionServerClientDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Index { get; set; }
    }

    /// <summary>
    /// VisionServer API 응답용 DTO (ShareLibrary.DTOs.RecipeDto와 동일 구조)
    /// </summary>
    private class VisionServerRecipeDto
    {
        public int RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public int ClientId { get; set; }
        public int RecipeIndex { get; set; }
    }
}
