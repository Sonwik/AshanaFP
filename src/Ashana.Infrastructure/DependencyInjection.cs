using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.State;
using Ashana.Infrastructure.Background;
using Ashana.Infrastructure.Messaging;
using Ashana.Infrastructure.Persistence;
using Ashana.Infrastructure.Security;
using Ashana.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashana.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers storage, security, messaging and background services.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        services.AddSingleton(storage);
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddOptions<WorkerOptions>().Bind(configuration.GetSection(WorkerOptions.SectionName));

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        // One object, two roles (Interface Segregation): services publish, components subscribe.
        services.AddSingleton<InProcessEventBus>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessEventBus>());
        services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<InProcessEventBus>());

        // Storage: the application layer only sees IStateStore (a snapshot plus pure transitions),
        // so a provider can be swapped by adding one more case here.
        services.AddSingleton(sp => new InMemoryStateStore(
            CanteenState.Empty(sp.GetRequiredService<IOptions<CanteenOptions>>().Value.TimeZone)));
        services.AddSingleton<IStateStore>(sp => sp.GetRequiredService<InMemoryStateStore>());
        switch (storage.Provider)
        {
            case StorageProvider.Memory:
                break;

            case StorageProvider.File:
                services.AddSingleton(sp => new FileSnapshotStore(
                    sp.GetRequiredService<InMemoryStateStore>(),
                    Path.IsPathRooted(storage.FilePath) ? storage.FilePath : Path.Combine(environment.ContentRootPath, storage.FilePath),
                    sp.GetRequiredService<ILogger<FileSnapshotStore>>()));
                services.AddHostedService(sp => sp.GetRequiredService<FileSnapshotStore>());
                break;

            default:
                throw new InvalidOperationException($"Unknown storage provider '{storage.Provider}'. Use Memory or File.");
        }

        services.AddSingleton<DemoDataSeeder>();
        services.AddSingleton<OrderLifecycleWorker>();
        if (configuration.GetValue($"{WorkerOptions.SectionName}:{nameof(WorkerOptions.Enabled)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<OrderLifecycleWorker>());
        }

        services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage");
        return services;
    }

    /// <summary>Loads stored data and seeds demo data into an empty store. Call once before the app starts.</summary>
    public static async Task InitializeStorageAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var file = services.GetService<FileSnapshotStore>();
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (seed.ResetOnStart)
        {
            var store = services.GetRequiredService<InMemoryStateStore>();
            store.Replace(CanteenState.Empty(store.Current.Settings.TimeZoneId));
        }
        else if (file is not null)
        {
            await file.LoadAsync(cancellationToken);
        }

        var created = await services.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
        if ((created || seed.ResetOnStart) && file is not null)
        {
            await file.SaveAsync(); // persist the fresh data right away
        }
    }
}
