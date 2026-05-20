using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ProductService : IProductService
{
    private readonly BodaVmsDbContext _db;

    public ProductService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Products
            .Include(p => p.DefaultRecipe)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        return await query
            .OrderBy(p => p.Code)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    public async Task<ProductDto?> GetByIdAsync(int id)
    {
        var entity = await _db.Products
            .Include(p => p.DefaultRecipe)
            .FirstOrDefaultAsync(p => p.Id == id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<ProductDto?> GetByCodeAsync(string code)
    {
        var entity = await _db.Products
            .Include(p => p.DefaultRecipe)
            .FirstOrDefaultAsync(p => p.Code == code);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<ProductDto> CreateAsync(ProductDto dto)
    {
        var entity = new Product
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Specification = dto.Specification,
            DefaultRecipeId = dto.DefaultRecipeId,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<ProductDto?> UpdateAsync(int id, ProductDto dto)
    {
        var entity = await _db.Products.FindAsync(id);
        if (entity is null) return null;

        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.Specification = dto.Specification;
        entity.DefaultRecipeId = dto.DefaultRecipeId;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var hasWorkOrders = await _db.WorkOrders.AnyAsync(w => w.ProductId == id);
        if (hasWorkOrders)
            throw new InvalidOperationException(
                "이 제품을 참조하는 WorkOrder가 있어 삭제할 수 없습니다. 대신 비활성화하세요.");

        var entity = await _db.Products.FindAsync(id);
        if (entity is null) return false;

        _db.Products.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ProductDto ToDto(Product e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Name = e.Name,
        Specification = e.Specification,
        DefaultRecipeId = e.DefaultRecipeId,
        DefaultRecipeName = e.DefaultRecipe?.Name,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
