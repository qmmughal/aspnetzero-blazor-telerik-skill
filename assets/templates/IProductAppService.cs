using System.Threading.Tasks;
using Abp.Application.Services;
using Abp.Application.Services.Dto;
using MyProject.Catalog.Dto;

namespace MyProject.Catalog;

// Extending IApplicationService is what publishes this at /api/services/app/Product/*
// — the WASM and MAUI clients depend on that endpoint existing.
public interface IProductAppService : IApplicationService
{
    Task<PagedResultDto<ProductDto>> GetAllAsync(GetProductsInput input);
    Task<CreateOrEditProductDto> GetForEditAsync(EntityDto<long> input);
    Task CreateOrEditAsync(CreateOrEditProductDto input);
    Task DeleteAsync(EntityDto<long> input);
}
