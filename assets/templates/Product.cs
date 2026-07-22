using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Abp.Domain.Entities;
using Abp.Domain.Entities.Auditing;

namespace MyProject.Catalog;

[Table("Products")]
public class Product : FullAuditedEntity<long>, IMustHaveTenant
{
    public const int MaxNameLength = 128;

    public virtual int TenantId { get; set; }

    [Required]
    [StringLength(MaxNameLength)]
    public virtual string Name { get; set; }

    public virtual decimal Price { get; set; }

    public virtual bool IsActive { get; set; }
}
