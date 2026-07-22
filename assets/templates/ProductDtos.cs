using System.ComponentModel.DataAnnotations;
using Abp.Application.Services.Dto;
using MyProject.Catalog;

namespace MyProject.Catalog.Dto;

public class ProductDto : EntityDto<long>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

public class GetProductsInput : PagedAndSortedResultRequestDto
{
    public string Filter { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>Nullable Id: null = create, set = edit. One DTO serves both, so one Razor form does too.</summary>
public class CreateOrEditProductDto
{
    public long? Id { get; set; }

    [Required]
    [StringLength(Product.MaxNameLength)]
    public string Name { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    public bool IsActive { get; set; }
}
