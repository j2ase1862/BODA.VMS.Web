using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IProductService
{
    Task<List<ProductDto>> GetAllAsync(bool includeInactive = false);
    Task<ProductDto?> GetByIdAsync(int id);
    Task<ProductDto?> GetByCodeAsync(string code);
    Task<ProductDto> CreateAsync(ProductDto dto);
    Task<ProductDto?> UpdateAsync(int id, ProductDto dto);
    Task<bool> DeleteAsync(int id);
}
