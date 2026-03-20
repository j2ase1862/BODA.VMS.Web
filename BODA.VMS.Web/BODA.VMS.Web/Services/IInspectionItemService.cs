using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IRecipeParameterService
{
    Task<List<RecipeParameterDto>> GetByRecipeAsync(int recipeId);
    Task<RecipeParameterDto?> GetByIdAsync(int id);
    Task<RecipeParameterDto> CreateAsync(RecipeParameterDto dto);
    Task<List<RecipeParameterDto>> CreateBatchAsync(List<RecipeParameterDto> dtos);
    Task<RecipeParameterDto?> UpdateAsync(int id, RecipeParameterDto dto);
    Task<bool> DeleteAsync(int id);
    Task<int> GetNextAvailableCodeAsync(int recipeId);
}
