using Microsoft.Extensions.DependencyInjection;

namespace Ashana.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services the UI components need. Both are <b>scoped</b>:
    /// in Blazor Server a scope is one browser tab (circuit), so every tab has its own toasts.
    /// </summary>
    public static IServiceCollection AddAshanaUi(this IServiceCollection services)
    {
        services.AddScoped<ToastService>();
        services.AddScoped<AshanaJs>();
        return services;
    }
}
