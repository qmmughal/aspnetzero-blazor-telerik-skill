using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Abp.Runtime.Session;
using Microsoft.Extensions.DependencyInjection;

namespace MyProject.Web.Blazor.Infrastructure;

/// <summary>
/// Blazor Server "scoped" means per-circuit, which can be hours. An AppService injected straight into
/// a component therefore holds one DbContext for the life of the page: stale reads, a growing change
/// tracker, ObjectDisposedException, concurrency errors on the second save.
///
/// Resolving a fresh scope per operation fixes that AND restores ABP's unit-of-work interception,
/// which circuit invocations otherwise bypass entirely.
///
/// Register as Singleton — it only holds the factory.
/// </summary>
public interface IScopedExecutor
{
    Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull;
    Task RunAsync<TService>(Func<TService, Task> action) where TService : notnull;
}

public class ScopedExecutor : IScopedExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICircuitPrincipalAccessor _circuitPrincipal;

    public ScopedExecutor(IServiceScopeFactory scopeFactory, ICircuitPrincipalAccessor circuitPrincipal)
    {
        _scopeFactory = scopeFactory;
        _circuitPrincipal = circuitPrincipal;
    }

    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        ApplyPrincipal(scope);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service);
    }

    public async Task RunAsync<TService>(Func<TService, Task> action) where TService : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        ApplyPrincipal(scope);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        await action(service);
    }

    // Without this, the new scope has no principal and the AppService call runs anonymous —
    // which in a multi-tenant app means AbpSession.TenantId is null and the data filter
    // returns the wrong rows. Verify by logging in as a tenant user.
    private void ApplyPrincipal(IServiceScope scope)
    {
        var accessor = scope.ServiceProvider.GetService<IPrincipalAccessor>();
        if (accessor is ScopedPrincipalAccessor settable && _circuitPrincipal.Principal is ClaimsPrincipal p)
        {
            settable.Principal = p;
        }
    }
}
