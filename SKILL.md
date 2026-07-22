---
name: aspnetzero-blazor-telerik
description: >
  Adds, converts, and reviews Blazor UI in ASP.NET Zero (ANZ) / ASP.NET Boilerplate solutions using
  Telerik UI for Blazor, on .NET 10+. Covers the full vertical slice — entity, ABP AppService, DTOs,
  AppPermissions, localization, multi-tenancy — plus the Blazor-into-ANZ graft: static web assets,
  middleware order, circuit-scoped DI, unit of work, AbpSession over SignalR, TelerikRootComponent,
  Metronic conflicts. Use whenever ASP.NET Zero, ANZ, ASP.NET Boilerplate, ABP, AppService,
  AppPermissions, Power Tools, Metronic, or a *.Web.Mvc / *.Application / *.Core.Shared project appears
  alongside anything Blazor or Telerik — including "add a Blazor page to my Zero project", "convert this
  DataTables/jQuery page to Blazor", "wire a TelerikGrid to my AppService", "blazor.server.js 404",
  "AbpSession.TenantId is null in my component", "DbContext disposed in my circuit", or any .razor work
  in a Zero solution. Prefer it over generic Blazor guidance whenever ABP or Telerik is involved, even
  without the words "ASP.NET Zero".
---

# ASP.NET Zero + Blazor + Telerik

Blazor in an ASP.NET Zero **web** solution is a graft, not a template. ANZ's official web UIs are
ASP.NET Core MVC & jQuery, Angular, and React — there is no first-party Blazor Server or Blazor
WebAssembly web UI. (ANZ's "Blazor" is the **MAUI Blazor Hybrid mobile app**, which is a different
thing entirely and is only offered for the MVC and Angular variants.)

That single fact explains most of the pain in this area. Because nobody at Volosoft wired Blazor into
the ANZ host for you, the seams — static web assets, middleware order, DI scope, unit of work, session —
are yours to get right, and they fail in ways that look like Blazor bugs but are actually ABP
lifetime-mismatch bugs. The job of this skill is to make those seams explicit so you stop rediscovering
them.

Read `references/integration-setup.md` before touching host wiring, `references/abp-backend.md` before
writing any AppService, and `references/telerik-patterns.md` before writing any Telerik markup.

---

## Step 0: Establish ground truth before writing anything

Guessing here is expensive, because ANZ, ABP, and Telerik have all had breaking changes recently and
the user's solution is very likely not on the newest of any of them. Inspect, don't assume:

1. **Versions.** Read the `.csproj` files and `Directory.Packages.props` if present.
   - `<TargetFramework>` — this skill targets `net10.0` and later. If you find `net8.0`/`net9.0`, say
     so and ask whether to target the current framework or match the project.
   - `Abp.*` package version → maps to an ANZ generation. ABP 11.x ≈ ANZ 15.x (.NET 10);
     ABP 10.x ≈ ANZ 14.x (.NET 9); ABP 9.x ≈ ANZ 13.x (.NET 8).
   - `Telerik.UI.for.Blazor` version. The API surface has drifted a lot between 7.x and 14.x —
     verify component parameter names against the installed version rather than writing from memory.
2. **Mapping strategy.** ANZ **15.2.0 migrated from AutoMapper to Mapperly**. Check which is present
   before writing DTO mapping — `ObjectMapper.Map<T>(...)` and a `[Mapper]` partial class are not
   interchangeable. See `references/abp-backend.md`.
3. **How Blazor is hosted here.** Look in the `*.Web.Mvc` (or `*.Web.Host`) `Program.cs`/`Startup.cs`:
   - `AddServerSideBlazor()` + `MapBlazorHub()` + `_Host.cshtml` → **legacy Blazor Server graft**.
   - `AddRazorComponents().AddInteractiveServerComponents()` + `MapRazorComponents<App>()` →
     **unified Blazor Web App graft** with `@rendermode InteractiveServer`.
   - `AddInteractiveWebAssemblyComponents()` or a separate `*.Blazor.Client` project → **WASM**, which
     cannot touch `IRepository` or `DbContext` directly and must go through ANZ's dynamic Web API.
   - Nothing yet → this is a greenfield integration. Do **not** silently pick a hosting model;
     surface the tradeoff (Server = direct AppService access and the SignalR circuit; WASM = HTTP hop,
     token handling, and no server-side ABP session) and let the user choose.
4. **Existing conventions.** Where do `.razor` files live? Is there a `TelerikRootComponent` already?
   Which Metronic demo/theme is active? Match what's there. A consistent codebase that deviates from
   this skill's defaults beats a half-converted one that follows them.

If the solution mixes signals or something contradicts the above, say what you found and ask. A wrong
guess about hosting model invalidates every line you then write.

---

## Step 0.5: Decide how deep to go

The user may want the whole slice or just the UI. Infer from the request, and state your read in one
line so they can correct it cheaply:

- **Full slice** — "add a Products page", "build CRUD for Invoice". Entity → EF config → migration →
  permissions → localization → AppService + DTOs → Razor component → menu.
- **UI only** — "wire a grid to `IProductAppService`", "make this page use TelerikGrid". The backend
  exists; touch it only if it genuinely cannot serve the UI (e.g. it returns `List<T>` where the grid
  needs a paged result — flag that rather than silently rewriting it).

When in doubt, ask which of the two, rather than doing the larger one and hoping.

**Before writing an AppService by hand, mention ANZ Power Tools.** It generates entity + DTOs +
AppService + permissions + localization from a JSON/entity definition, and staying inside it keeps the
codebase regenerable. Hand-writing the backend is the right call when the entity is unusual or Power
Tools output has already been customized — but the user should make that choice knowingly. Power Tools
does not generate Blazor UI, so the Razor layer is hand-written either way.

---

## The three seams that break ANZ + Blazor Server

These are lifetime mismatches between ABP (built around a per-HTTP-request pipeline) and Blazor Server
(built around a long-lived SignalR circuit). Understanding *why* they break matters more than
memorizing the fix, because they resurface in new shapes.

### 1. Scoped services outlive the operation

In MVC, a scoped `IProductAppService` — and the `DbContext` behind it — lives for one request and dies.
In Blazor Server, "scoped" means **the lifetime of the circuit**: potentially hours. Inject an
AppService straight into a component with `@inject` and you are holding one `DbContext` across every
interaction on that page. Symptoms: stale reads that ignore other users' writes, a growing change
tracker, `ObjectDisposedException`, and concurrency exceptions on the second save.

Resolve a fresh scope per operation instead:

```razor
@inject IServiceScopeFactory ScopeFactory

@code {
    private async Task<PagedResultDto<ProductDto>> LoadAsync(GetProductsInput input)
    {
        using var scope = ScopeFactory.CreateScope();
        var appService = scope.ServiceProvider.GetRequiredService<IProductAppService>();
        return await appService.GetAllAsync(input);
    }
}
```

`references/integration-setup.md` shows how to wrap this in a small helper so every component isn't
repeating the boilerplate.

### 2. There is no ambient unit of work

ABP opens a unit of work via its MVC/Web API filters. A SignalR circuit method invocation goes through
no such filter, so `CurrentUnitOfWork` is null and lazy-loaded navigation properties throw. Either call
through the AppService interface (whose `[UnitOfWork]` convention applies when resolved from a scope,
which is the main reason to prefer the pattern above) or open one explicitly with
`IUnitOfWorkManager.Begin()`. Do not reach past the AppService into `IRepository` from a component —
that is where the missing-UoW failures cluster.

### 3. AbpSession is empty over the circuit

`ClaimsAbpSession` reads from `IPrincipalAccessor`, which reads `IHttpContextAccessor`. Over a
long-lived circuit there is no meaningful current `HttpContext`, so `AbpSession.UserId` and
`.TenantId` come back null — and in a multi-tenant ANZ app a null `TenantId` silently changes which
rows the data filter returns. Capture the principal at circuit start from
`AuthenticationStateProvider` and flow it into ABP's principal accessor. Details and code in
`references/integration-setup.md`.

**In review mode, check these three first.** They produce the most damaging and least obvious bugs, and
in a multi-tenant app #3 is a data-isolation issue, not merely a defect.

---

## ANZ backend conventions (non-negotiable defaults)

Deviate only where the solution already deviates consistently. Full code in
`references/abp-backend.md`.

- **Project placement.** DTOs and the service *interface* go in `*.Application.Shared`; the
  implementation in `*.Application`; the entity in `*.Core`; EF config and migrations in
  `*.EntityFrameworkCore`; permission name constants in `*.Core.Shared`.
- **AppService** derives from the solution's `{Project}AppServiceBase`, implements an interface that
  extends `IApplicationService` (this is what generates the dynamic Web API endpoint the WASM and
  mobile clients need), and returns DTOs — never entities.
- **Authorization** is declarative: `[AbpAuthorize(AppPermissions.Pages_Products)]` on the class and
  the narrower `_Create` / `_Edit` / `_Delete` constants on the methods. Every new permission must be
  added to `AppPermissions` **and** registered in `AppAuthorizationProvider`, or it silently never
  grants. On the UI side gate with `IPermissionChecker` / `IsGranted` — but treat that as cosmetic;
  server-side attributes are the actual boundary.
- **Localization** through `L("Key")` against `AppConsts.LocalizationSourceName`, with keys added to
  the XML source under `*.Core/Localization/`. No hardcoded display strings in Razor markup, including
  grid column titles, validation messages, and button labels.
- **Multi-tenancy**: implement `IMustHaveTenant` (or `IMayHaveTenant`) on the entity so ABP's data
  filter applies automatically. Never hand-filter by `TenantId` in a query — that both duplicates the
  filter and hides bugs when the filter is disabled.
- **Paging and sorting** use ABP's `PagedResultDto<T>` with an input deriving from
  `PagedAndSortedResultRequestDto`, and the `.WhereIf(...)` / `.OrderBy(input.Sorting)` /
  `.PageBy(input)` chain over `IQueryable`. Sorting relies on Dynamic LINQ string parsing, which is why
  the Telerik grid's sort descriptors have to be translated into that string form rather than applied
  client-side.
- **Auditing**: prefer `FullAuditedEntity<TKey>` unless there's a reason not to; ANZ's audit log and
  entity-change-history UIs depend on it.

---

## Telerik conventions

Full patterns, including the grid-to-AppService bridge, in `references/telerik-patterns.md`.

- **`AddTelerikBlazor()` in DI and a single `<TelerikRootComponent>` wrapping the layout body.**
  Components that render outside it (popups, windows, tooltips) will misposition or not render at all.
  This is the first thing to check when "the Telerik dialog doesn't appear".
- **Bind the grid with `OnRead`, never `Data`, for anything ANZ-backed.** `Data` implies the full set
  is in memory, which discards server-side paging, sorting, and — importantly — lets a large tenant
  table become a memory problem. `OnRead` maps cleanly onto `PagedResultDto`: set `args.Data` from
  `Items` and `args.Total` from `TotalCount`. Translating `args.Request.Sorts` into the Dynamic LINQ
  string ANZ expects is the fiddly part; there's a reusable helper in the reference file.
- **Licensing.** Recent Telerik versions require a license key (a `telerik-license.txt` in the user
  profile or a `TELERIK_LICENSE` environment variable) at build time. A build failing with a licensing
  message is a machine/CI setup issue, not a code issue — say so rather than editing code around it.
- **Theme coexistence.** ANZ ships Metronic on Bootstrap 5. Telerik's Bootstrap theme collides least;
  the default theme will fight Metronic's form and button styling. Load the Telerik theme *after*
  Metronic and expect to scope a few overrides. Don't restyle Metronic to suit Telerik.
- **Verify parameter names against the installed version.** Toolbar, filter, and editing APIs have all
  changed across major versions. If the Telerik MCP server or docs are available, check there; if not,
  read the package's own metadata rather than writing from memory and letting the user find out at
  compile time.

---

## Workflow: scaffold a new entity end-to-end

1. Confirm entity name, fields, types, nullability, and whether it is tenant-scoped. Ask rather than
   invent — a wrong key type means a wrong migration.
2. Entity in `*.Core` + `DbSet` on the DbContext + EF configuration + migration in
   `*.EntityFrameworkCore`. Generate the migration command for the user; don't run it blind.
3. Permission constants in `AppPermissions`, registration in `AppAuthorizationProvider`, localization
   keys in the XML source.
4. DTOs + `IProductAppService` in `*.Application.Shared`; `ProductAppService` in `*.Application`,
   with `GetAll` returning `PagedResultDto<ProductDto>`.
5. Razor page under the project's Blazor folder, with `@attribute [Authorize]`, the correct render
   mode for the detected hosting model, `TelerikGrid` bound via `OnRead`, and create/edit in a
   `TelerikWindow` or dedicated page depending on existing convention.
6. Menu entry in `AppNavigationProvider` with the matching permission name.
7. Report what you changed, and list the manual steps you deliberately left to the user (running the
   migration, adding the license key, restarting to pick up localization).

Starter files in `assets/templates/` — copy and adapt rather than composing from scratch.

## Workflow: convert an existing MVC/jQuery page to Blazor

ANZ's MVC pages are Razor views + a `_ViewModel` + a `.js` file driving DataTables and Metronic
modals, all talking to an existing AppService via the dynamic Web API. The AppService is the asset
here: it almost always survives the conversion untouched.

1. Read the existing `.cshtml`, its `.js`, and the AppService it calls. Enumerate every behavior —
   filters, bulk actions, Excel export, permission-gated buttons, confirmation dialogs. Conversions
   fail by quietly dropping features, not by getting the grid wrong.
2. Map DataTables' server-side parameters onto the Telerik grid's `OnRead` equivalents. Both are
   already server-paged against the same input DTO, so this is a translation, not a redesign.
3. Replace Metronic/SweetAlert modals with `TelerikWindow` / `TelerikDialog`, keeping the same
   confirmation semantics.
4. Keep the same permission names and localization keys. Reusing them is what makes the converted page
   drop into the existing role setup unchanged.
5. Leave the old page in place until the user confirms parity, and tell them explicitly what still
   needs verifying.

## Workflow: review existing Blazor + Telerik code

Walk `references/checklist.md`. Report findings grouped by severity, and lead with the three seams
above plus multi-tenancy leaks — those are correctness and isolation issues. Convention and style
findings come after. When asked to *review*, report and propose; rewrite only when asked to *fix*.

---

## Reference files

- `references/integration-setup.md` — hosting Blazor inside the ANZ web host: csproj web assets,
  middleware ordering, `_Host`/`App.razor`, circuit-scoped DI helper, unit of work, AbpSession flow,
  Telerik registration, WASM-specific notes
- `references/abp-backend.md` — entity, repository, AppService, DTOs, permissions, localization,
  multi-tenancy, AutoMapper vs Mapperly
- `references/telerik-patterns.md` — grid `OnRead` ↔ `PagedResultDto` bridge, sort/filter translation,
  forms and validation, windows and dialogs, version-drift notes
- `references/checklist.md` — the review-mode checklist
- `assets/templates/` — starter Entity, DTOs, AppService, and Razor list/edit components
