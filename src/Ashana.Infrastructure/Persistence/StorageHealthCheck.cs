using Ashana.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ashana.Infrastructure.Persistence;

/// <summary>/health reports the state of the data store (used by monitoring and containers).</summary>
public sealed class StorageHealthCheck(IStateStore store) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = store.Current;
        return Task.FromResult(HealthCheckResult.Healthy(
            $"Store reachable, accepting orders: {state.Settings.AcceptingOrders}",
            new Dictionary<string, object> { ["orders"] = state.Orders.Count, ["dishes"] = state.MenuItems.Count }));
    }
}
