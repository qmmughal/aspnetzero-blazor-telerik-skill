# Starter templates

Copy and adapt — these are the shape, not the final answer. Replace `MyProject`, `Product`,
and `Catalog` throughout, and verify Telerik parameter names against the installed version
(see `references/telerik-patterns.md` §1).

| File | Goes in |
|---|---|
| `Product.cs` | `*.Core/Catalog/` |
| `ProductDtos.cs` | `*.Application.Shared/Catalog/Dto/` |
| `IProductAppService.cs` | `*.Application.Shared/Catalog/` |
| `ProductAppService.cs` | `*.Application/Catalog/` |
| `ScopedExecutor.cs` | `*.Web.Mvc/Blazor/Infrastructure/` — register as Singleton |
| `GridRequestMapper.cs` | `*.Web.Mvc/Blazor/Infrastructure/` |
| `ProductList.razor` | `*.Web.Mvc/Blazor/Pages/Catalog/` |
| `ProductEditWindow.razor` | `*.Web.Mvc/Blazor/Pages/Catalog/` |

Not included, because they must be edited in place rather than added: `AppPermissions`,
`AppAuthorizationProvider`, `AppNavigationProvider`, the DbContext, and the localization XML.
