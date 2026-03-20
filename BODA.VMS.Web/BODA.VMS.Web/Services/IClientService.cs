using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IClientService
{
    Task<List<ClientDto>> GetAllClientsAsync();
    Task<ClientDto?> GetClientByIdAsync(int id);
    Task<ClientDto> CreateClientAsync(ClientDto dto);
    Task<ClientDto?> UpdateClientAsync(int id, ClientDto dto);
    Task<bool> DeleteClientAsync(int id);
    Task<List<RecipeDto>> GetRecipesByClientIdAsync(int clientId);
    Task<RecipeDto> CreateRecipeAsync(int clientId, RecipeDto dto);
    Task<bool> DeleteRecipeAsync(int recipeId);
}
