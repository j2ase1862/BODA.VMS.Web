using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class RecipeParameterService : IRecipeParameterService
{
    private readonly BodaVmsDbContext _db;

    public RecipeParameterService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<RecipeParameterDto>> GetByRecipeAsync(int recipeId)
    {
        return await _db.RecipeParameters
            .Where(p => p.RecipeId == recipeId)
            .OrderBy(p => p.ParamCode)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    public async Task<RecipeParameterDto?> GetByIdAsync(int id)
    {
        var entity = await _db.RecipeParameters.FindAsync(id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<RecipeParameterDto> CreateAsync(RecipeParameterDto dto)
    {
        var entity = new RecipeParameter
        {
            RecipeId = dto.RecipeId,
            ParamCode = dto.ParamCode,
            ParamValue = dto.ParamValue,
            Description = dto.Description,
            Category = dto.Category,
            Unit = dto.Unit,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.RecipeParameters.Add(entity);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<List<RecipeParameterDto>> CreateBatchAsync(List<RecipeParameterDto> dtos)
    {
        var entities = dtos.Select(dto => new RecipeParameter
        {
            RecipeId = dto.RecipeId,
            ParamCode = dto.ParamCode,
            ParamValue = dto.ParamValue,
            Description = dto.Description,
            Category = dto.Category,
            Unit = dto.Unit,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _db.RecipeParameters.AddRange(entities);
        await _db.SaveChangesAsync();
        return entities.Select(ToDto).ToList();
    }

    public async Task<RecipeParameterDto?> UpdateAsync(int id, RecipeParameterDto dto)
    {
        var entity = await _db.RecipeParameters.FindAsync(id);
        if (entity is null) return null;

        entity.ParamCode = dto.ParamCode;
        entity.ParamValue = dto.ParamValue;
        entity.Description = dto.Description;
        entity.Category = dto.Category;
        entity.Unit = dto.Unit;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.RecipeParameters.FindAsync(id);
        if (entity is null) return false;

        _db.RecipeParameters.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetNextAvailableCodeAsync(int recipeId)
    {
        var maxCode = await _db.RecipeParameters
            .Where(p => p.RecipeId == recipeId)
            .MaxAsync(p => (int?)p.ParamCode) ?? 0;
        return maxCode + 1;
    }

    private static RecipeParameterDto ToDto(RecipeParameter entity)
    {
        return new RecipeParameterDto
        {
            Id = entity.Id,
            RecipeId = entity.RecipeId,
            ParamCode = entity.ParamCode,
            ParamValue = entity.ParamValue,
            Description = entity.Description,
            Category = entity.Category,
            Unit = entity.Unit,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
