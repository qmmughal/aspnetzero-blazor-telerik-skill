using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Abp.Application.Services.Dto;
using Abp.Authorization;
using Abp.Domain.Repositories;
using Abp.Extensions;
using Abp.Linq.Extensions;
using Microsoft.EntityFrameworkCore;
using MyProject.Authorization;
using MyProject.Catalog.Dto;
using System.Linq.Dynamic.Core;

namespace MyProject.Catalog;

[AbpAuthorize(AppPermissions.Pages_Products)]
public class ProductAppService : MyProjectAppServiceBase, IProductAppService
{
    private readonly IRepository<Product, long> _productRepository;

    public ProductAppService(IRepository<Product, long> productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<PagedResultDto<ProductDto>> GetAllAsync(GetProductsInput input)
    {
        var query = _productRepository.GetAll()
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(), p => p.Name.Contains(input.Filter))
            .WhereIf(input.IsActive.HasValue, p => p.IsActive == input.IsActive.Value);

        // Count the filtered query BEFORE paging — counting after PageBy returns the page size.
        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(input.Sorting ?? "id desc")   // Dynamic LINQ throws on null; unsorted paging is non-deterministic
            .PageBy(input)
            .ToListAsync();

        // ANZ >= 15.2 uses Mapperly instead of ObjectMapper — check which before copying this line.
        return new PagedResultDto<ProductDto>(totalCount, ObjectMapper.Map<List<ProductDto>>(items));
    }

    [AbpAuthorize(AppPermissions.Pages_Products_Edit)]
    public async Task<CreateOrEditProductDto> GetForEditAsync(EntityDto<long> input)
    {
        var product = await _productRepository.GetAsync(input.Id);
        return ObjectMapper.Map<CreateOrEditProductDto>(product);
    }

    [AbpAuthorize(AppPermissions.Pages_Products_Create, AppPermissions.Pages_Products_Edit)]
    public async Task CreateOrEditAsync(CreateOrEditProductDto input)
    {
        if (input.Id.HasValue)
        {
            var product = await _productRepository.GetAsync(input.Id.Value);
            ObjectMapper.Map(input, product);
        }
        else
        {
            var product = ObjectMapper.Map<Product>(input);
            product.TenantId = AbpSession.GetTenantId();
            await _productRepository.InsertAsync(product);
        }
    }

    [AbpAuthorize(AppPermissions.Pages_Products_Delete)]
    public async Task DeleteAsync(EntityDto<long> input)
    {
        await _productRepository.DeleteAsync(input.Id);
    }
}
