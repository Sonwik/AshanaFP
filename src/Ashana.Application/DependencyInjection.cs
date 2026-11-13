using Ashana.Application.Analytics;
using Ashana.Application.Common;
using Ashana.Application.Kitchen;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Application.Settings;
using Ashana.Application.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ashana.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use-case services of the application layer.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CanteenOptions>().Bind(configuration.GetSection(CanteenOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System); // tests replace it with a fake clock
        services.AddSingleton<CanteenClock>();

        // Services are stateless (all state lives in the IStateStore snapshot), so singletons are safe and cheap.
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MenuService>();
        services.AddSingleton<SlotService>();
        services.AddSingleton<OrderService>();
        services.AddSingleton<KitchenService>();
        services.AddSingleton<AnalyticsService>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<UserAdminService>();
        return services;
    }
}
