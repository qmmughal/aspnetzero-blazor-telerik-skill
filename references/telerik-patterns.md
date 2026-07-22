# Telerik UI for Blazor patterns in an ASP.NET Zero app

Contents:
1. Version drift — read this first
2. The grid ↔ AppService bridge (`OnRead`)
3. Translating sorts and filters
4. Forms, validation, and the CreateOrEdit DTO
5. Windows, dialogs, and confirmations
6. Notifications and error surfacing
7. Permission-gated UI
8. Excel export
9. Performance notes

---

## 1. Version drift — read this first

Telerik UI for Blazor has moved from 7.x to 14.x, and parameter names, toolbar components, and
filtering APIs changed along the way. Writing markup from memory produces code that compiles on the
version you remember and not the one installed.

Before writing Telerik markup:

- Read the `Telerik.UI.for.Blazor` version from the `.csproj` or `Directory.Packages.props`.
- If the Telerik MCP server is connected, query it — it's version-aware and exists for this problem.
- Otherwise check the installed package's own API surface, or tell the user which version you assumed.

Known drift points worth checking rather than assuming: the grid toolbar component name, filter-mode
enum members, `OnRead` argument shapes, and command-button parameter names. When unsure, say what you
verified and what you didn't.

The code below shows the *shape* of each pattern. Adapt names to the installed version.

---

## 2. The grid ↔ AppService bridge (`OnRead`)

This is the single most important pattern in the skill. ANZ AppServices are already server-paged;
`OnRead` is the grid binding that matches that. Binding with `Data` pulls the whole table into memory,
which discards paging and turns a large tenant's data into a memory problem.

```razor
@inject IScopedExecutor Scoped
@inject ILocalizationSource L

<TelerikGrid TItem="ProductDto"
             OnRead="@OnReadAsync"
             Pageable="true" PageSize="20"
             Sortable="true"
             FilterMode="@GridFilterMode.FilterRow">
    <GridColumns>
        <GridColumn Field="@nameof(ProductDto.Name)"  Title="@L.GetString("Name")" />
        <GridColumn Field="@nameof(ProductDto.Price)" Title="@L.GetString("Price")"
                    DisplayFormat="{0:C2}" />
        <GridCommandColumn Width="160px">
            <GridCommandButton Command="Edit" OnClick="@EditAsync">
                @L.GetString("Edit")
            </GridCommandButton>
        </GridCommandColumn>
    </GridColumns>
</TelerikGrid>

@code {
    private async Task OnReadAsync(GridReadEventArgs args)
    {
        var input = new GetProductsInput
        {
            SkipCount     = args.Request.Skip,
            MaxResultCount = args.Request.PageSize,
            Sorting       = GridRequestMapper.ToAbpSorting(args.Request.Sorts, "id desc"),
            Filter        = GridRequestMapper.ExtractSimpleFilter(args.Request.Filters, nameof(ProductDto.Name))
        };

        var result = await Scoped.RunAsync<IProductAppService, PagedResultDto<ProductDto>>(
            s => s.GetAllAsync(input));

        args.Data  = result.Items;
        args.Total = result.TotalCount;
    }
}
```

Why each piece:

- **`Scoped.RunAsync`** rather than `@inject IProductAppService` — see `integration-setup.md` §6. Every
  grid read gets a fresh `DbContext`, which is what makes the grid show other users' writes.
- **`args.Total` from `TotalCount`, not `Items.Count`.** Using the page's item count makes the pager
  show one page forever, which looks like a paging bug and is really a binding bug.
- **`args.Request.Skip`** maps to `SkipCount` directly; don't recompute it from `Page * PageSize`, since
  the two disagree when the page size changes mid-session.

---

## 3. Translating sorts and filters

ANZ's `Sorting` is a Dynamic LINQ string. Telerik gives structured descriptors. Write the translation
once in a shared static helper rather than inline in each component — every grid needs it, and the
fallback-when-empty rule is easy to forget.

```csharp
public static class GridRequestMapper
{
    public static string ToAbpSorting(IEnumerable<SortDescriptor> sorts, string fallback)
    {
        var clauses = sorts?
            .Select(s => $"{s.Member} {(s.SortDirection == ListSortDirection.Descending ? "desc" : "asc")}")
            .ToList();

        return clauses is { Count: > 0 } ? string.Join(", ", clauses) : fallback;
    }
}
```

Two cautions:

- **Never pass a user-controlled string straight into `OrderBy`.** Dynamic LINQ parses expressions, so
  an arbitrary string is an injection surface. `SortDescriptor.Member` comes from your own column
  definitions, which is safe — but if you ever accept a sort field from a query string or an API caller,
  validate it against an allow-list of property names first.
- **Always supply a fallback.** Dynamic LINQ throws on an empty string, and an unsorted paged query
  returns non-deterministic pages.

For filtering, the honest options are: (a) keep the grid's filter row simple and map one or two fields
onto explicit DTO properties, as above; or (b) add structured filter properties to the input DTO for
each filterable column. Trying to translate Telerik's full composite filter tree into Dynamic LINQ is
where this pattern turns into a maintenance problem — if the user wants rich filtering, extending the
input DTO is the more durable answer, and worth saying out loud rather than quietly building a parser.

---

## 4. Forms, validation, and the CreateOrEdit DTO

ANZ's `CreateOrEditProductDto` with a nullable `Id` lets one form serve both operations. Validation
attributes on the DTO drive both client-side display and the server's own model validation, so they
only need writing once.

```razor
<EditForm Model="@Model" OnValidSubmit="@SaveAsync">
    <DataAnnotationsValidator />

    <TelerikTextBox @bind-Value="@Model.Name" />
    <ValidationMessage For="@(() => Model.Name)" />

    <TelerikNumericTextBox @bind-Value="@Model.Price" Format="C2" Min="0" />
    <ValidationMessage For="@(() => Model.Price)" />

    <TelerikButton ButtonType="ButtonType.Submit" ThemeColor="primary">
        @L.GetString("Save")
    </TelerikButton>
</EditForm>
```

- Telerik's inputs work inside a standard `EditForm` — you don't need `TelerikForm` unless you want its
  layout features. Mixing `TelerikForm` with a hand-built `EditForm` in the same component is a common
  source of double-validation confusion; pick one per form.
- Client validation is convenience. The AppService validates again (ABP runs DataAnnotations on input
  DTOs automatically), so don't skip DTO attributes because the UI already checks.
- Catch `UserFriendlyException` around the save and surface `.Message` — that's ABP's channel for
  business-rule failures, and dropping it leaves the user staring at a form that did nothing.

---

## 5. Windows, dialogs, and confirmations

ANZ's MVC pages use Metronic modals and SweetAlert confirmations. The Blazor equivalents are
`TelerikWindow` (for the create/edit form) and `TelerikDialog`/`DialogFactory` (for confirmations).
Preserve the same semantics on conversion — particularly that delete is confirmed, which users rely on.

```razor
@inject DialogFactory Dialogs

@code {
    private async Task DeleteAsync(ProductDto product)
    {
        var confirmed = await Dialogs.ConfirmAsync(
            L.GetString("AreYouSureToDelete", product.Name),
            L.GetString("Confirm"));

        if (!confirmed) return;

        await Scoped.RunAsync<IProductAppService>(s => s.DeleteAsync(new EntityDto<long>(product.Id)));
        await GridRef.Rebind();
    }
}
```

`Rebind()` re-runs `OnRead` against the server, which is what you want after a mutation — mutating a
local list instead leaves the total count and the current page's contents wrong.

Both `TelerikWindow` and the dialog factory render into `TelerikRootComponent`. If neither appears,
check that first (`integration-setup.md` §9).

---

## 6. Notifications and error surfacing

`TelerikNotification` replaces ANZ's `abp.notify.success(...)`. Keep messages localized, and make sure
failures are visible: a silent catch around a save is worse than an unhandled exception, because the
user believes the save succeeded.

```csharp
try
{
    await Scoped.RunAsync<IProductAppService>(s => s.CreateOrEditAsync(Model));
    NotificationRef.Show(new NotificationModel {
        Text = L.GetString("SavedSuccessfully"), ThemeColor = ThemeConstants.Notification.ThemeColor.Success });
}
catch (UserFriendlyException ex)
{
    NotificationRef.Show(new NotificationModel {
        Text = ex.Message, ThemeColor = ThemeConstants.Notification.ThemeColor.Error, CloseAfter = 0 });
}
```

---

## 7. Permission-gated UI

Mirror the AppService's permissions so users don't see buttons that will 403:

```razor
@if (canEdit)
{
    <GridCommandButton Command="Edit" OnClick="@EditAsync">@L.GetString("Edit")</GridCommandButton>
}

@code {
    private bool canEdit;

    protected override async Task OnInitializedAsync()
    {
        canEdit = await Scoped.RunAsync<IPermissionChecker, bool>(
            p => p.IsGrantedAsync(AppPermissions.Pages_Products_Edit));
    }
}
```

Treat this as cosmetic. `[AbpAuthorize]` on the AppService is the real boundary; hiding a button is a
usability improvement, not a security control. Reviews should confirm the server-side attribute exists
whenever they see a UI permission check — a UI check without one is the actual vulnerability.

---

## 8. Excel export

The grid's built-in export operates on the data the grid currently holds, which with `OnRead` is one
page. Users converting from an ANZ MVC page will expect "export all", because ANZ's MVC export goes
through the AppService and returns a `FileDto`.

Match the existing behaviour: add an export method to the AppService (ANZ's `IExcelExporter` pattern
returns a `FileDto` with a download URL) and wire the toolbar button to it, rather than using the
grid's client-side export and quietly shipping one page. Say which you did.

---

## 9. Performance notes

- `OnRead` + server paging is the baseline. Don't add row virtualization on top of it as a first move —
  they solve different problems and combining them complicates the read logic.
- Each grid interaction over Blazor Server is a SignalR round trip. Debounce filter-row input rather
  than reading on every keystroke.
- Keep the DTO narrow. Every column you don't display is bytes over the circuit on every page change.
- Prefer `@key` on rows keyed by entity id so the diff stays stable across rebinds.
