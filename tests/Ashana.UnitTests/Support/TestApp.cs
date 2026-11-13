using Ashana.Application;
using Ashana.Application.Abstractions;
using Ashana.Application.Analytics;
using Ashana.Application.Common;
using Ashana.Application.Kitchen;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Application.Settings;
using Ashana.Application.State;
using Ashana.Application.Users;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;
using Ashana.Infrastructure;
using Ashana.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ashana.UnitTests.Support;

/// <summary>Ids of the small data set every service test starts with.</summary>
public sealed record Seeded(
    Guid StudentId,
    Guid Student2Id,
    Guid CookId,
    Guid AdminId,
    Guid MainsId,
    Guid DrinksId,
    Guid PlovId,
    Guid LagmanId,
    Guid TeaId,
    Guid SoupId);

/// <summary>
/// The real application and infrastructure services (the same registrations the web app uses),
/// with in-memory storage, a controllable clock and a recording event publisher.
/// </summary>
public sealed class TestApp : IAsyncDisposable
{
    public const string Password = "Passw0rd2026";

    /// <summary>PBKDF2 is slow on purpose; hash the shared test password once per test run.</summary>
    private static readonly Lazy<string> PasswordHash = new(() => new AspNetPasswordHasher().Hash(Password));

    private readonly ServiceProvider _provider;

    private TestApp(ServiceProvider provider, ManualTimeProvider time, RecordingPublisher events)
    {
        _provider = provider;
        Time = time;
        Events = events;
    }

    public ManualTimeProvider Time { get; }

    public RecordingPublisher Events { get; }

    public OrderService Orders => Get<OrderService>();

    public KitchenService Kitchen => Get<KitchenService>();

    public MenuService Menu => Get<MenuService>();

    public SettingsService Settings => Get<SettingsService>();

    public SlotService Slots => Get<SlotService>();

    public AccountService Accounts => Get<AccountService>();

    public UserAdminService UserAdmin => Get<UserAdminService>();

    public AnalyticsService Analytics => Get<AnalyticsService>();

    public CanteenClock Clock => Get<CanteenClock>();

    public static TestApp Create(DateTimeOffset? now = null, IReadOnlyDictionary<string, string?>? config = null)
    {
        var values = new Dictionary<string, string?>
        {
            // An id the operating system does not know: CanteenClock falls back to a fixed UTC+5 zone.
            ["Canteen:TimeZone"] = "Ashana/Test",
            ["Canteen:FallbackUtcOffsetHours"] = "5",
            ["Storage:Provider"] = "Memory",
            ["Seed:DemoData"] = "false",
            ["Worker:Enabled"] = "false",
        };

        foreach (var (key, value) in config ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var time = new ManualTimeProvider(now ?? TestData.Now);
        var events = new RecordingPublisher();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration, new TestHostEnvironment());
        services.AddSingleton(events);
        services.AddSingleton<IEventPublisher>(events); // replaces the in-process bus: events are checked synchronously

        return new TestApp(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }), time, events);
    }

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>The state store; tests read <see cref="IStateStore.Current"/> to check what was committed.</summary>
    public IStateStore Store => Get<IStateStore>();

    public CanteenState State => Store.Current;

    /// <summary>Two students, a cook, an admin, two categories and four dishes (one already sold out).</summary>
    public async Task<Seeded> SeedAsync()
    {
        var serviceDay = SlotPlanner.ServiceDay(Clock.Now, State.Rules);
        var now = Clock.Now;

        User NewUser(string email, string name, UserRole role) => User.Create(email, name, PasswordHash.Value, role, now).Value;

        MenuItem NewDish(Guid categoryId, string name, decimal price, int stock, MenuTags tags, string description) =>
            MenuItem.Create(new MenuItemDetails(categoryId, name, description, price, "🍽️", null, null, tags, 10, stock), serviceDay).Value;

        var student = NewUser("student@test.kz", "Aruzhan Bekova", UserRole.Student);
        var student2 = NewUser("student2@test.kz", "Timur Akhmetov", UserRole.Student);
        var cook = NewUser("cook@test.kz", "Marat Kenzhebayev", UserRole.Cook);
        var admin = NewUser("admin@test.kz", "Saule Nurlanovna", UserRole.Admin);

        var mains = Category.Create("Mains", "🍛", 1).Value;
        var drinks = Category.Create("Drinks", "🍵", 2).Value;

        var plov = NewDish(mains.Id, "Plov", 1800m, 10, MenuTags.Halal, "Rice slow-cooked with beef and carrots");
        var lagman = NewDish(mains.Id, "Lagman", 1700m, 3, MenuTags.Spicy | MenuTags.Halal, "Hand-pulled noodles in a rich sauce");
        var tea = NewDish(drinks.Id, "Black tea", 300m, 50, MenuTags.Vegetarian, "With milk");
        var soup = NewDish(mains.Id, "Lentil soup", 900m, 0, MenuTags.Vegetarian, "Sold out from the start");

        var seeded = await Store.UpdateAsync(state => Result.Success(Change.Of(
            state
                .With(state.Settings.MarkStockReset(serviceDay).GetOrElse(state.Settings))
                .With(student).With(student2).With(cook).With(admin)
                .With(mains).With(drinks)
                .WithAll([plov, lagman, tea, soup]),
            Unit.Value)));
        Assert.True(seeded.IsSuccess);

        return new Seeded(student.Id, student2.Id, cook.Id, admin.Id, mains.Id, drinks.Id, plov.Id, lagman.Id, tea.Id, soup.Id);
    }

    public async Task<Guid> AddUserAsync(string email, UserRole role = UserRole.Student, string name = "Test Student")
    {
        var user = User.Create(email, name, PasswordHash.Value, role, Clock.Now).Value;
        await Store.UpdateAsync(state => Result.Success(Change.Of(state.With(user), Unit.Value)));
        return user.Id;
    }

    public static DateTimeOffset At(int hour, int minute, int dayOffset = 0) => TestData.At(hour, minute, dayOffset);

    public static PlaceOrderCommand Command(Guid customerId, DateTimeOffset slotStart, params (Guid Item, int Quantity, decimal Price)[] lines) =>
        new(
            customerId,
            lines.Select(l => new CartItemRequest(l.Item, l.Quantity, l.Price)).ToList(),
            slotStart,
            PaymentMethod.Card,
            null,
            Guid.NewGuid());

    /// <summary>Places an order and fails the test if it is refused.</summary>
    public async Task<OrderDto> PlaceAsync(Guid customerId, DateTimeOffset slotStart, params (Guid Item, int Quantity, decimal Price)[] lines)
    {
        var result = await Orders.PlaceOrderAsync(Command(customerId, slotStart, lines));
        Assert.True(result.IsSuccess, result.IsFailure ? $"Order refused: {result.Error}" : null);
        return result.Value;
    }

    public Task<MenuItem> DishAsync(Guid id) =>
        Task.FromResult(State.FindItem(id).GetOrElse(() => throw new InvalidOperationException($"Dish {id} not found")));

    public Task<Order> OrderAsync(Guid id) =>
        Task.FromResult(State.FindOrder(id).GetOrElse(() => throw new InvalidOperationException($"Order {id} not found")));

    public Task<User> UserAsync(Guid id) =>
        Task.FromResult(State.FindUser(id).GetOrElse(() => throw new InvalidOperationException($"User {id} not found")));

    /// <summary>Changes canteen rules for a test (for example one order per slot).</summary>
    public async Task SetRulesAsync(Func<CanteenRules, CanteenRules> change)
    {
        var current = await Settings.GetRulesAsync();
        var result = await Settings.UpdateAsync(change(current));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "Ashana.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
