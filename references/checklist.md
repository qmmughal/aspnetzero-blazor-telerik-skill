# Review checklist

Work top to bottom. The order is deliberate: the first two sections are correctness and data-isolation
issues that can be actively harmful, and the rest are quality. Report findings grouped by severity, each
with a one-line explanation of *why* it matters and the concrete fix.

---

## A. Data isolation and security (report first, always)

- [ ] Does `AbpSession.TenantId` actually resolve inside components? If the principal isn't flowed into
      the circuit, a tenant user may be seeing rows outside their tenant. Verify by logging in as a
      tenant user, not by reading code. Treat a confirmed leak as an incident, not a code-quality note.
- [ ] Does every AppService method have an `[AbpAuthorize]` attribute with the right permission?
- [ ] Are permissions registered in **both** `AppPermissions` and `AppAuthorizationProvider`?
- [ ] Is there any UI permission check without a matching server-side attribute? The UI check is
      cosmetic; the missing attribute is the vulnerability.
- [ ] Any hand-written `WHERE TenantId = ...`? It duplicates ABP's filter and hides the case where the
      filter isn't applied.
- [ ] Any `DisableFilter` call — is its scope as tight as it can be, and justified?
- [ ] Any user-controlled string reaching `.OrderBy(...)`? Dynamic LINQ parses expressions; sort fields
      from outside your own column definitions need an allow-list.

## B. Lifetime correctness (the three seams)

- [ ] Are AppServices injected directly into components with `@inject`? That holds one `DbContext` for
      the whole circuit — stale reads, disposed contexts, concurrency errors on second save.
- [ ] Is a fresh scope created per operation?
- [ ] Is `IRepository` or `DbContext` touched from a component? There's no ambient unit of work there.
- [ ] Any explicit `IUnitOfWorkManager.Begin()` without a matching `CompleteAsync()`? Silent rollback.
- [ ] Does the principal get re-applied inside each new scope, or only at circuit open? Only at circuit
      open means the AppService call runs anonymous.
- [ ] Are `IDisposable`/`IAsyncDisposable` implemented on components that subscribe to events, timers,
      or `NavigationManager.LocationChanged`? Circuits are long-lived, so leaks accumulate.

## C. Host integration

- [ ] `RequiresAspNetWebAssets` present if the Blazor script 404s?
- [ ] `UseStaticFiles` before the Blazor endpoints; `UseAuthentication` before `MapBlazorHub`?
- [ ] `UseJwtTokenMiddleware` still between authn and authz?
- [ ] Is `MapFallbackToPage` route-constrained, or is it swallowing ANZ's MVC routes?
- [ ] On the unified graft, does every interactive page have a render mode? A page without one is
      static SSR and its buttons do nothing — correct by design, confusing in this context.
- [ ] Exactly one `TelerikRootComponent`, wrapping the layout body?

## D. ANZ conventions

- [ ] DTOs and service interfaces in `*.Application.Shared`, not `*.Application`? (Breaks client builds.)
- [ ] Does the service interface extend `IApplicationService`? Without it there's no dynamic Web API.
- [ ] Are entities ever returned from an AppService instead of DTOs?
- [ ] Does mapping match the ANZ version — `ObjectMapper` on ≤15.1, Mapperly on ≥15.2? Leftover
      `ObjectMapper` calls in a 15.2+ solution signal an incomplete upgrade.
- [ ] `FullAuditedEntity` used where audit history is expected?
- [ ] Are business-rule failures thrown as `UserFriendlyException` so the message reaches the user?
- [ ] Is `base.OnModelCreating(builder)` still called in the DbContext?

## E. Localization

- [ ] Any hardcoded display strings? Check the usual leak points specifically: grid column titles,
      empty-state text, dialog and button labels, notification messages, validation text.
- [ ] Are new keys present in the XML source, and did anyone mention the restart needed to pick them up?

## F. Telerik usage

- [ ] Grid bound with `OnRead`, not `Data`, for anything ANZ-backed?
- [ ] `args.Total` set from `TotalCount` rather than the page's item count?
- [ ] Is `Sorting` given a fallback for the empty-sort case?
- [ ] Does the grid rebind after mutations, rather than patching a local list?
- [ ] Do the component parameters actually exist in the installed Telerik version, or were they written
      from memory?
- [ ] Does "export" export everything, or silently just the current page? Match the MVC page it replaced.

## G. Conversion parity (when reviewing a converted MVC page)

- [ ] Enumerate the old page's behaviours — filters, bulk actions, export, permission-gated buttons,
      confirmations — and confirm each one survived. Conversions fail by dropping features quietly.
- [ ] Same permission names and localization keys as the original? Reusing them is what lets the new
      page drop into the existing role configuration unchanged.
- [ ] Is the old page still in place until parity is confirmed?

## H. Component hygiene

- [ ] Data loaded in `OnInitializedAsync`/`OnParametersSetAsync`, never in the constructor?
- [ ] `[Parameter]` properties are plain auto-properties — no `required` modifier, no `init` accessor
      (Blazor sets parameters by reflection, which bypasses both). Use `[EditorRequired]` instead.
- [ ] `@code` blocks over roughly 50 lines moved to a `.razor.cs` partial?
- [ ] `StateHasChanged()` only where the render pipeline wouldn't otherwise run?
