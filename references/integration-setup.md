# Hosting Blazor inside the ASP.NET Zero web host

Contents:
1. Why this is a graft
2. csproj: static web assets
3. Service registration order
4. Middleware ordering
5. Entry point: `_Host.cshtml` vs `App.razor`
6. Circuit-scoped DI helper
7. Unit of work
8. Flowing AbpSession into the circuit
9. Telerik registration and theme coexistence
10. Blazor WebAssembly variant
11. Diagnosing common failures

---

## 1. Why this is a graft

ASP.NET Zero's web UIs are MVC & jQuery, Angular, and React. There is no first-party Blazor web UI, so
no ANZ template has pre-wired the pieces below. Everything here is integration work that the ANZ
project templates do for their supported UIs but not for Blazor.

Practical consequence: when something breaks, check the graft before suspecting Blazor or ABP. Almost
every "Blazor doesn't work in Zero" report traces back to one of sections 2, 4, 6, 7, or 8.

---

## 2. csproj: static web assets

`blazor.server.js` (and `blazor.web.js` on the unified template) ship as **static web assets** inside
the framework package rather than as files in `wwwroot`. An ANZ `*.Web.Mvc` project is a plain
`Microsoft.NET.Sdk.Web` project, but depending on how the solution was assembled the static-web-asset
pipeline may not run for it — the script then 404s at runtime with no build error.

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

Verify after building that `obj/Debug/net10.0/staticwebassets*.json` exists and references the Blazor
script. If the script still 404s, the cause is almost always section 4 (static files served before the
asset provider is registered) rather than this property.

From .NET 10 the script is served fingerprinted and compressed. If you have hardcoded a `<script
src="_framework/blazor.server.js">` path with a manual cache-busting query string, remove the query
string and let the framework's import map handle it.

---

## 3. Service registration order

Blazor and Telerik registration must happen **before** `AddAbp...`/ABP module initialization completes,
because ABP replaces parts of the DI container during initialization.

```csharp
// *.Web.Mvc/Startup.cs (or Program.cs)
services.AddControllersWithViews();          // ANZ MVC, keep
services.AddRazorPages();                    // needed for _Host.cshtml on the legacy graft

services.AddServerSideBlazor();              // legacy Blazor Server graft
// -- or, unified Blazor Web App graft --
// services.AddRazorComponents().AddInteractiveServerComponents();

services.AddTelerikBlazor();

services.AddHttpContextAccessor();           // ANZ registers this, but confirm — section 8 needs it

return services.AddAbp<MyProjectWebMvcModule>(options => { /* ANZ default config */ });
```

Do not register an AppService as a Blazor `Scoped` service to "make injection easier". ABP already
registers it conventionally, and re-registering changes its lifetime semantics — see section 6.

---

## 4. Middleware ordering

This is the most common source of silent failures. ANZ's pipeline has ABP-specific middleware that
must keep its relative position, and the Blazor endpoints have to land in the right place around it.

```csharp
app.UseAbpRequestLocalization();     // must precede anything that renders localized text
app.UseStaticFiles();                // must precede the Blazor hub, or the script 404s
app.UseRouting();

app.UseAuthentication();
app.UseJwtTokenMiddleware();         // ANZ-specific, must sit between authn and authz
app.UseAuthorization();

app.UseAbpRequestLocalization();     // ANZ calls this again after authn so user culture applies

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute("defaultWithArea", "{area}/{controller=Home}/{action=Index}/{id?}");
    endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

    endpoints.MapRazorPages();
    endpoints.MapBlazorHub();                    // legacy graft
    endpoints.MapFallbackToPage("/blazor/{*path:nonfile}", "/_Host");
    // -- or, unified graft --
    // endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode();
});
```

Ordering rules worth understanding rather than copying:

- **`UseStaticFiles` before the Blazor hub.** The hub negotiate request is fine either way, but the
  script that initiates it is a static asset.
- **`UseAuthentication` before `MapBlazorHub`.** The circuit's initial HTTP handshake is where the
  principal is established; if authentication hasn't run, every circuit starts anonymous and section 8
  cannot recover it.
- **`UseJwtTokenMiddleware` stays between authn and authz.** ANZ relies on it to translate its bearer
  tokens; moving it breaks API auth for the rest of the app, not just Blazor.
- **Scope the fallback.** `MapFallbackToPage("/_Host")` without a route constraint swallows ANZ's MVC
  routes and 404 handling. Constrain it to the Blazor area's path prefix as above.

---

## 5. Entry point

### Legacy Blazor Server graft — `Pages/_Host.cshtml`

Reuse ANZ's layout so the Metronic chrome, culture, and antiforgery setup stay consistent:

```cshtml
@page "/blazor"
@namespace MyProject.Web.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@{ Layout = "~/Views/Shared/_Layout.cshtml"; }

<component type="typeof(App)" render-mode="ServerPrerendered" />
<script src="_framework/blazor.server.js"></script>
```

Prerendering runs the component **outside** the circuit, so section 8's principal flow is not yet in
place and section 6's scope factory resolves against the HTTP request scope. Components that read
`AbpSession` during prerender will see different values than after the circuit connects. If that
matters, use `render-mode="Server"` (no prerender) or guard first-render logic with
`OnAfterRenderAsync(firstRender)`.

### Unified Blazor Web App graft — `App.razor` + `Routes.razor`

Standard .NET 8+ shape. Each interactive page needs `@rendermode InteractiveServer`; a page without a
render mode is static SSR and its buttons will do nothing. That is by design, not a bug — but in a
grafted ANZ app it is a frequent point of confusion, because the surrounding MVC pages *are* meant to
be non-interactive.

---

## 6. Circuit-scoped DI helper

Restating the problem from SKILL.md: Blazor Server "scoped" means per-circuit, so an `@inject`ed
AppService holds one `DbContext` for the life of the page. Rather than repeating
`CreateScope()` in every component, provide a helper and inject that:

```csharp
public interface IScopedExecutor
{
    Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull;
    Task RunAsync<TService>(Func<TService, Task> action) where TService : notnull;
}

public class ScopedExecutor(IServiceScopeFactory scopeFactory) : IScopedExecutor
{
    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service);
    }

    public async Task RunAsync<TService>(Func<TService, Task> action) where TService : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        await action(service);
    }
}
```

Register it as `Singleton` (it only holds the factory) and use it from components:

```razor
@inject IScopedExecutor Scoped

@code {
    private async Task DeleteAsync(long id) =>
        await Scoped.RunAsync<IProductAppService>(s => s.DeleteAsync(new EntityDto<long>(id)));
}
```

The value here is not the abstraction, it's that the scope boundary becomes visible at every call site,
which is what stops the lifetime bug from creeping back in.

---

## 7. Unit of work

ABP begins a unit of work through its MVC/Web API action filters. SignalR circuit invocations bypass
those filters entirely, so there is no ambient UoW.

- **Calling through the AppService interface is the normal path.** ABP's conventional UoW interception
  applies to `IApplicationService` implementations, so resolving the service from a fresh scope
  (section 6) gets you a UoW around the call.
- **Reaching into `IRepository` from a component does not.** Lazy-loaded navigation properties throw,
  and `GetAll()` returns an `IQueryable` over a connection nobody owns. Don't do it.
- **When you genuinely need a UoW spanning several calls** — a multi-step save that must be atomic —
  open it explicitly and complete it:

```csharp
using var scope = scopeFactory.CreateScope();
var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
using var uow = uowManager.Begin();

var products = scope.ServiceProvider.GetRequiredService<IProductAppService>();
await products.CreateAsync(dto);
await products.ReorderAsync(otherDto);

await uow.CompleteAsync();   // omitting this rolls everything back, silently
```

Forgetting `CompleteAsync()` is the classic bug: no exception, no data.

---

## 8. Flowing AbpSession into the circuit

`ClaimsAbpSession` resolves the current user through `IPrincipalAccessor` → `IHttpContextAccessor`.
Over a long-lived circuit there is no meaningful current `HttpContext`, so `AbpSession.UserId` and
`AbpSession.TenantId` come back null.

`TenantId` is the dangerous one. ABP's `IMustHaveTenant` data filter uses it, so a null tenant in a
multi-tenant ANZ app changes which rows a query returns — quietly, with no error.

Capture the principal when the circuit starts and make it available to ABP:

```csharp
public class AbpCircuitHandler(
    AuthenticationStateProvider authStateProvider,
    IPrincipalAccessor principalAccessor) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        // ScopedPrincipalAccessor exposes a settable Principal; ABP's default
        // HttpContextPrincipalAccessor does not. Register the scoped variant.
        if (principalAccessor is ScopedPrincipalAccessor scoped)
        {
            scoped.Principal = state.User;
        }
    }
}
```

Register with `services.AddScoped<CircuitHandler, AbpCircuitHandler>()` and ensure
`IPrincipalAccessor` resolves to a settable implementation for the Blazor path.

Because section 6 creates a *new* scope per operation, the principal must be re-applied inside that
scope too — otherwise the AppService call runs anonymous even though the circuit knows who the user is.
Fold that into the `ScopedExecutor`: capture the `ClaimsPrincipal` once per circuit, then set it on
each new scope's principal accessor before resolving the service.

**Always verify this end to end in a multi-tenant app**: log in as a tenant user, hit a Blazor page
that lists a tenant-scoped entity, and confirm it shows that tenant's rows and no others. A skill-built
page that hasn't been checked this way should be reported as unverified, not as done.

---

## 9. Telerik registration and theme coexistence

`services.AddTelerikBlazor();` in DI, plus assets in the host page/layout:

```html
<link rel="stylesheet"
      href="_content/Telerik.UI.for.Blazor/css/kendo-theme-bootstrap/all.css" />
<script src="_content/Telerik.UI.for.Blazor/js/telerik-blazor.js" defer></script>
```

And a single root component wrapping the layout body:

```razor
<TelerikRootComponent>
    @Body
</TelerikRootComponent>
```

Popups, windows, dropdowns, and tooltips are rendered into that root. Without it they misposition or
don't appear — which is what "the Telerik dialog does nothing" almost always means.

**Theme.** ANZ ships Metronic 8 on Bootstrap 5. Telerik's **Bootstrap** theme collides least; the
default theme fights Metronic on form controls, buttons, and spacing. Load Telerik's CSS after
Metronic's so its cascade wins where they overlap, and scope any additional overrides to a wrapper
class rather than editing Metronic.

**Licensing.** Recent Telerik versions require a license key at build time — `telerik-license.txt` in
the user profile, or the `TELERIK_LICENSE` environment variable in CI. A build error mentioning
licensing is an environment problem; don't work around it in code.

**Version drift.** Between Telerik 7.x and 14.x, toolbar, filtering, and editing APIs have changed
names and shapes. Check the installed version's API rather than writing from memory. Telerik publishes
an MCP server for exactly this; if it's connected, use it.

---

## 10. Blazor WebAssembly variant

WASM has no access to `IRepository`, `DbContext`, `AbpSession`, or `IPermissionChecker` — it runs in the
browser. Everything goes over HTTP.

- Call ANZ's **dynamic Web API**, which exposes every `IApplicationService` at
  `/api/services/app/{Service}/{Method}` automatically. Generating a typed client (NSwag against ANZ's
  Swagger endpoint) beats hand-rolling `HttpClient` calls.
- Send ANZ's bearer token on every request, and — critically for multi-tenancy — the
  `Abp.TenantId` header. Omitting it makes the server resolve the host tenant.
- Sections 6, 7, and 8 do not apply: there is no circuit, no ambient UoW, no server-side session. The
  tradeoff is that permission checks are now purely a server concern, so any UI gating in WASM is
  cosmetic and the `[AbpAuthorize]` attributes are doing all the real work.
- ANZ's response envelope (`{ result, success, error, unAuthorizedRequest }`) is not a plain payload.
  Unwrap it and surface `error.message`; a client that treats HTTP 200 as success will show blank
  screens on business-rule failures.

---

## 11. Diagnosing common failures

| Symptom | Most likely cause |
|---|---|
| `blazor.server.js` 404 | `RequiresAspNetWebAssets` missing (§2) or `UseStaticFiles` misplaced (§4) |
| Circuit connects, then MVC pages 404 | Unconstrained `MapFallbackToPage` (§4) |
| Buttons do nothing, no errors | No `@rendermode` on the unified graft (§5) |
| `ObjectDisposedException` on DbContext | AppService injected directly into the component (§6) |
| Stale data until page refresh | Same — one `DbContext` for the whole circuit (§6) |
| Save appears to work, nothing persists | Explicit UoW never completed (§7) |
| Lazy-loaded navigation property throws | No ambient UoW; repository accessed from component (§7) |
| `AbpSession.UserId` null in a component | Principal not flowed into the circuit (§8) |
| Tenant sees wrong rows / host rows | `AbpSession.TenantId` null, data filter misapplied (§8) — treat as a data-isolation incident |
| Telerik popup/window never appears | Missing or misplaced `TelerikRootComponent` (§9) |
| Build fails with a licensing message | Telerik license key not present on the machine (§9) |
| Component parameter "does not exist" | Telerik version drift; verify against installed version (§9) |
