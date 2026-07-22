# ASP.NET Zero backend conventions

Contents:
1. Where things live
2. Entity
3. DbContext, EF configuration, migration
4. Permissions
5. Localization
6. DTOs
7. AppService
8. Mapping: AutoMapper vs Mapperly
9. Multi-tenancy
10. Features and editions
11. Power Tools

---

## 1. Where things live

ANZ's layering is enforced by project references, so putting a type in the wrong project produces a
compile error at best and a circular-reference dead end at worst.

| Artifact | Project |
|---|---|
| Entity, domain services, `AppAuthorizationProvider`, localization XML | `*.Core` |
| Permission name constants (`AppPermissions`), enums, consts | `*.Core.Shared` |
| DTOs, `IProductAppService` | `*.Application.Shared` |
| `ProductAppService`, mapping profiles | `*.Application` |
| `DbContext`, EF configuration, migrations, repositories | `*.EntityFrameworkCore` |
| Controllers, views, Blazor components, `AppNavigationProvider` | `*.Web.Mvc` (or `*.Web.Host`) |

The `*.Shared` split exists so mobile and WASM clients can reference contracts without dragging in the
server implementation. Putting a DTO in `*.Application` instead of `*.Application.Shared` is the most
common placement mistake and only shows up when someone tries to build the client.

---

## 2. Entity

```csharp
using Abp.Domain.Entities;
using Abp.Domain.Entities.Auditing;

namespace MyProject.Catalog;

[Table("Products")]
public class Product : FullAuditedEntity<long>, IMustHaveTenant
{
    public const int MaxNameLength = 128;

    public virtual int TenantId { get; set; }

    [Required, StringLength(MaxNameLength)]
    public virtual string Name { get; set; }

    public virtual decimal Price { get; set; }

    public virtual bool IsActive { get; set; }
}
```

- `FullAuditedEntity<TKey>` gives creation/modification/deletion audit columns and soft delete. ANZ's
  audit-log and entity-change-history UIs depend on those columns being there, so prefer it unless
  there is a reason not to.
- `IMustHaveTenant` (required tenant) or `IMayHaveTenant` (nullable — host-owned rows allowed) activates
  ABP's automatic data filter. Choose deliberately; changing it later is a migration.
- `virtual` on properties keeps EF lazy loading and ABP's proxying working.
- Length constants as `public const int` on the entity let the DTO, EF configuration, and validation all
  reference one number.

---

## 3. DbContext, EF configuration, migration

```csharp
// *.EntityFrameworkCore/EntityFrameworkCore/MyProjectDbContext.cs
public virtual DbSet<Product> Products { get; set; }
```

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);   // never omit — ABP configures its own entities here

    builder.Entity<Product>(b =>
    {
        b.HasIndex(e => new { e.TenantId, e.Name });
        b.Property(e => e.Price).HasPrecision(18, 2);
    });
}
```

Index on `(TenantId, ...)` rather than the column alone: every query carries the tenant filter, so a
non-leading `TenantId` gives the optimizer little to work with.

Generate the migration from the `*.EntityFrameworkCore` project with the web project as startup:

```
dotnet ef migrations add Added_Product --project src/MyProject.EntityFrameworkCore --startup-project src/MyProject.Web.Mvc
```

Give the user the command; don't run migrations against their database unattended.

---

## 4. Permissions

Two files, both required — adding to one and not the other means the permission silently never grants.

```csharp
// *.Core.Shared/Authorization/AppPermissions.cs
public const string Pages_Products = "Pages.Products";
public const string Pages_Products_Create = "Pages.Products.Create";
public const string Pages_Products_Edit   = "Pages.Products.Edit";
public const string Pages_Products_Delete = "Pages.Products.Delete";
```

```csharp
// *.Core/Authorization/AppAuthorizationProvider.cs
var products = pages.CreateChildPermission(
    AppPermissions.Pages_Products, L("Products"),
    multiTenancySides: MultiTenancySides.Host | MultiTenancySides.Tenant);

products.CreateChildPermission(AppPermissions.Pages_Products_Create, L("CreateNewProduct"));
products.CreateChildPermission(AppPermissions.Pages_Products_Edit,   L("EditProduct"));
products.CreateChildPermission(AppPermissions.Pages_Products_Delete, L("DeleteProduct"));
```

`multiTenancySides` controls which side can even be granted the permission. Getting it wrong means the
permission doesn't appear in the role editor for the side that needs it — a confusing symptom, because
the code looks correct.

---

## 5. Localization

Keys go in the XML source under `*.Core/Localization/MyProject/MyProject.xml` (and the per-language
files). Reference them with `L("Key")`.

```xml
<text name="Products">Products</text>
<text name="CreateNewProduct">Create new product</text>
<text name="ProductDeletedSuccessfully">Product deleted successfully</text>
```

In Razor, inject the localizer ANZ already provides rather than hardcoding strings:

```razor
@inject ILocalizationSource L
<TelerikGridColumn Field="@nameof(ProductDto.Name)" Title="@L.GetString("Name")" />
```

This matters more than it looks: grid column titles, empty-state text, dialog buttons, and validation
messages are the places English leaks into a localized ANZ app, and they're all easy to miss in review.
Localization XML changes require an app restart to take effect — say so when handing off.

---

## 6. DTOs

```csharp
// *.Application.Shared/Catalog/Dto/ProductDto.cs
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

public class CreateOrEditProductDto
{
    public long? Id { get; set; }   // null = create, set = edit

    [Required, StringLength(Product.MaxNameLength)]
    public string Name { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    public bool IsActive { get; set; }
}
```

The single `CreateOrEditDto` with a nullable `Id` is ANZ's own convention (Power Tools generates it),
and it's what lets one Razor form serve both create and edit. Validation attributes live here, not on
the entity, because this is what `DataAnnotationsValidator` and Telerik's form validation see.

`PagedAndSortedResultRequestDto` supplies `SkipCount`, `MaxResultCount`, and `Sorting`. `Sorting` is a
Dynamic-LINQ string (`"Name asc"`), which is why the Telerik grid's sort descriptors need translating —
see `telerik-patterns.md`.

---

## 7. AppService

```csharp
[AbpAuthorize(AppPermissions.Pages_Products)]
public class ProductAppService : MyProjectAppServiceBase, IProductAppService
{
    private readonly IRepository<Product, long> _productRepository;

    public ProductAppService(IRepository<Product, long> productRepository)
        => _productRepository = productRepository;

    public async Task<PagedResultDto<ProductDto>> GetAllAsync(GetProductsInput input)
    {
        var query = _productRepository.GetAll()
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(), p => p.Name.Contains(input.Filter))
            .WhereIf(input.IsActive.HasValue, p => p.IsActive == input.IsActive.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(input.Sorting ?? "id desc")
            .PageBy(input)
            .ToListAsync();

        return new PagedResultDto<ProductDto>(totalCount, ObjectMapper.Map<List<ProductDto>>(items));
    }

    [AbpAuthorize(AppPermissions.Pages_Products_Create, AppPermissions.Pages_Products_Edit)]
    public async Task CreateOrEditAsync(CreateOrEditProductDto input)
    {
        if (input.Id.HasValue) await UpdateAsync(input);
        else await CreateAsync(input);
    }

    [AbpAuthorize(AppPermissions.Pages_Products_Delete)]
    public async Task DeleteAsync(EntityDto<long> input)
        => await _productRepository.DeleteAsync(input.Id);
}
```

Points that matter:

- **Interface extends `IApplicationService`.** That's what makes ABP publish it as a dynamic Web API
  endpoint at `/api/services/app/Product/GetAll` — which WASM and MAUI clients depend on.
- **Count before paging**, on the filtered query. Counting after `PageBy` returns the page size.
- **`.OrderBy(input.Sorting ?? "...")` needs a fallback.** Dynamic LINQ throws on null, and an unsorted
  paged query returns non-deterministic pages.
- **Return DTOs, never entities.** Serializing an entity drags navigation properties and audit fields
  across the wire and defeats the layering.
- **Business-rule failures use `UserFriendlyException`.** ABP surfaces its message to the client; a
  bare `Exception` shows a generic error instead.
- The class-level `[AbpAuthorize]` is the floor; method attributes narrow it. Both apply.

---

## 8. Mapping: AutoMapper vs Mapperly

**ANZ 15.2.0 replaced AutoMapper with Mapperly.** Check which one the solution uses before writing
mapping code — the two are not interchangeable.

AutoMapper (ANZ ≤ 15.1): configuration in a `CustomDtoMapper`, runtime mapping via
`ObjectMapper.Map<TDest>(source)`.

```csharp
configuration.CreateMap<Product, ProductDto>();
configuration.CreateMap<CreateOrEditProductDto, Product>();
```

Mapperly (ANZ ≥ 15.2): source-generated, so mappings are declared as partial methods on a `[Mapper]`
class and resolved at compile time.

```csharp
[Mapper]
public partial class ProductMapper
{
    public partial ProductDto ToDto(Product source);
    public partial List<ProductDto> ToDtoList(List<Product> source);
}
```

The practical difference: Mapperly errors are **compile-time**, so an unmapped property is caught at
build rather than producing a silently-null field. If you find `ObjectMapper.Map` calls in an
ANZ 15.2+ solution, that's leftover from an incomplete upgrade — flag it rather than adding more.

---

## 9. Multi-tenancy

- ABP's data filter applies automatically to `IMustHaveTenant`/`IMayHaveTenant` entities using
  `AbpSession.TenantId`. **Never hand-filter by `TenantId` in a query.** It duplicates the filter and
  masks the case where the filter isn't active — which is exactly the case you want to fail loudly.
- To operate across tenants deliberately, disable the filter explicitly and scope it tightly:

```csharp
using (CurrentUnitOfWork.DisableFilter(AbpDataFilters.MayHaveTenant))
{
    // host-side cross-tenant work only
}
```

- `[MultiTenancySide(MultiTenancySides.Host)]` on an AppService restricts it to the host side.
- **In Blazor Server this is the highest-risk area.** If `AbpSession.TenantId` is null because the
  principal never reached the circuit, the filter behaves differently and a tenant user can see rows
  that aren't theirs. See `integration-setup.md` §8, and verify by logging in as a tenant user rather
  than assuming.

---

## 10. Features and editions

For SaaS-gated functionality, define the feature in `AppFeatureProvider`, name it in `AppFeatures`, and
gate with `[RequiresFeature(AppFeatures.ProductManagement)]` on the AppService, plus
`IFeatureChecker.IsEnabledAsync(...)` for UI gating. Feature gating is orthogonal to permissions —
a user can hold the permission while their tenant's edition lacks the feature.

---

## 11. Power Tools

ANZ's Power Tools generate entity, DTOs, AppService, permissions, localization entries, and MVC/Angular/
React UI from an entity definition. It does **not** generate Blazor UI.

Mention it before hand-writing a backend slice, because generated code stays regenerable and matches
the rest of the solution exactly. Hand-writing is the right call when the entity is unusual, when
generated code has already been customized (regenerating would overwrite it), or when only the UI layer
is in scope. Either way, the Blazor components are hand-written — so a common good split is: Power
Tools for the backend, this skill for the Razor and Telerik layer.
