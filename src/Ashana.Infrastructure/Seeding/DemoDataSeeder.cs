using System.Collections.Immutable;
using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashana.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Fill an empty store with a demo menu, demo accounts and two weeks of order history.</summary>
    public bool DemoData { get; set; } = true;

    /// <summary>Days of generated order history (for the analytics dashboard).</summary>
    public int HistoryDays { get; set; } = 14;

    /// <summary>Delete all data on start and seed fresh demo data (handy right before a demo).</summary>
    public bool ResetOnStart { get; set; }
}

/// <summary>Demo accounts printed in the README and on the sign-in page.</summary>
public static class DemoAccounts
{
    public const string Password = "Ashana2026";

    public const string AdminEmail = "admin@ashana.test";
    public const string CookEmail = "cook@ashana.test";
    public const string StudentEmail = "student@ashana.test";
    public const string SecondStudentEmail = "student2@ashana.test";
}

/// <summary>How much demo data was created.</summary>
public sealed record SeedSummary(int Users, int Dishes, int PastOrders, int ActiveOrders);

/// <summary>
/// Creates demo data through the same domain functions the app uses, so seeded data obeys every rule.
/// The demo state is built as a value and committed in one step, and only into an empty store.
/// Randomness uses a fixed seed: every fresh start produces the same history, which keeps demos repeatable.
/// </summary>
public sealed class DemoDataSeeder
{
    private const int OrderNumberStart = 100;

    private static readonly Error NotEmpty = Error.Conflict("seed.not_empty", "The store already has data.");

    private readonly IStateStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly CanteenClock _clock;
    private readonly SeedOptions _options;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(IStateStore store, IPasswordHasher hasher, CanteenClock clock, IOptions<SeedOptions> options, ILogger<DemoDataSeeder> logger)
    {
        _store = store;
        _hasher = hasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <returns><c>true</c> if data was created.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.DemoData || !_store.Current.Users.IsEmpty)
        {
            return false; // never touch a store that already has data
        }

        var hash = _hasher.Hash(DemoAccounts.Password); // the slow, impure part happens once, outside
        var now = _clock.Now;
        var seeded = await _store.UpdateAsync(
            state => state.Users.IsEmpty
                ? Result.Success(Build(state, hash, now, _clock.TimeZone, _options.HistoryDays, new Random(2026)))
                : Result.Failure<Change<SeedSummary>>(NotEmpty),
            cancellationToken);

        return seeded.Match(
            summary =>
            {
                _logger.LogInformation(
                    "Demo data created: {Users} users, {Items} dishes, {History} past orders, {Active} active orders",
                    summary.Users, summary.Dishes, summary.PastOrders, summary.ActiveOrders);
                return true;
            },
            _ => false);
    }

    /// <summary>Builds the whole demo state from an empty one. Deterministic for a given random seed.</summary>
    public static Change<SeedSummary> Build(CanteenState empty, string passwordHash, DateTimeOffset now, TimeZoneInfo zone, int historyDays, Random random)
    {
        ArgumentNullException.ThrowIfNull(empty);
        var rules = empty.Rules;
        var serviceDay = SlotPlanner.ServiceDay(now, rules);

        var users = CreateUsers(passwordHash, now);
        var categories = CreateCategories();
        var items = CreateMenu(categories, serviceDay);
        var students = users.Where(u => u.Role == UserRole.Student).ToList();

        var history = CreateHistory(items, students, rules, zone, serviceDay, historyDays, random);
        var (active, reservedItems) = CreateActiveOrders(items, students, rules, zone, now, serviceDay, random);

        var state = empty
            .With(empty.Settings.MarkStockReset(serviceDay).GetOrElse(empty.Settings))
            .Pipe(s => users.Aggregate(s, (acc, u) => acc.With(u)))
            .Pipe(s => categories.Values.Aggregate(s, (acc, c) => acc.With(c)))
            .WithAll(reservedItems)
            .WithAll(history)
            .WithAll(active);

        return Change.Of(state, new SeedSummary(users.Count, items.Count, history.Count, active.Count));
    }

    private static List<User> CreateUsers(string hash, DateTimeOffset now)
    {
        (string Email, string Name, UserRole Role)[] people =
        [
            (DemoAccounts.AdminEmail, "Saule Nurlanovna", UserRole.Admin),
            (DemoAccounts.CookEmail, "Marat Kenzhebayev", UserRole.Cook),
            (DemoAccounts.StudentEmail, "Aruzhan Bekova", UserRole.Student),
            (DemoAccounts.SecondStudentEmail, "Timur Akhmetov", UserRole.Student),
            ("dana@ashana.test", "Dana Serikova", UserRole.Student),
            ("nurlan@ashana.test", "Nurlan Abenov", UserRole.Student),
            ("aigerim@ashana.test", "Aigerim Tulegenova", UserRole.Student),
            ("ilyas@ashana.test", "Ilyas Omarov", UserRole.Student),
            ("madina@ashana.test", "Madina Zhaksylykova", UserRole.Student),
            ("arman@ashana.test", "Arman Saparov", UserRole.Student),
            ("kamila@ashana.test", "Kamila Ismailova", UserRole.Student),
            ("yerlan@ashana.test", "Yerlan Mukanov", UserRole.Student),
        ];

        return [.. people.Select(p => User.Create(p.Email, p.Name, hash, p.Role, now.AddDays(-30)).Value)];
    }

    private static ImmutableDictionary<string, Category> CreateCategories()
    {
        (string Key, string Name, string Icon)[] list =
        [
            ("mains", "Mains", "🍛"),
            ("soups", "Soups", "🍲"),
            ("salads", "Salads", "🥗"),
            ("pastry", "Pastry", "🥐"),
            ("desserts", "Desserts", "🍰"),
            ("drinks", "Drinks", "🍵"),
        ];

        return list
            .Select((c, index) => (c.Key, Category: Category.Create(c.Name, c.Icon, index + 1).Value))
            .ToImmutableDictionary(x => x.Key, x => x.Category, StringComparer.Ordinal);
    }

    private static List<MenuItem> CreateMenu(ImmutableDictionary<string, Category> c, DateOnly serviceDay)
    {
        const MenuTags H = MenuTags.Halal;
        const MenuTags V = MenuTags.Vegetarian;

        (string Category, string Emoji, string Name, string Description, decimal Price, int Calories, MenuTags Tags, int Prep, int Stock)[] menu =
        [
            ("mains", "🍛", "Plov", "Rice slow-cooked in a kazan with beef, carrots and chickpeas.", 1800, 720, H, 5, 60),
            ("mains", "🍜", "Lagman", "Hand-pulled noodles with beef and a rich vegetable sauce.", 1700, 650, H | MenuTags.Spicy, 8, 45),
            ("mains", "🥟", "Manty, 4 pcs", "Steamed dumplings with minced beef and onion, served with sour cream.", 1600, 580, H, 10, 40),
            ("mains", "🍲", "Beshbarmak", "Tender boiled beef over flat noodles with onion sauce.", 2200, 780, H, 6, 25),
            ("mains", "🍗", "Chicken cutlet with mash", "Juicy chicken cutlet with creamy mashed potatoes.", 1500, 610, H, 7, 50),
            ("mains", "🥘", "Vegetable stew with rice", "Seasonal vegetables stewed with herbs, served with rice.", 1200, 430, V, 5, 30),
            ("mains", "🍝", "Creamy chicken pasta", "Penne with chicken and mushrooms in a light cream sauce.", 1600, 690, H | MenuTags.New, 9, 30),
            ("soups", "🍲", "Sorpa", "Clear beef broth with potatoes and fresh herbs.", 1000, 260, H, 3, 40),
            ("soups", "🥣", "Borscht", "Beetroot soup with beef, served with sour cream.", 1000, 320, H, 3, 35),
            ("soups", "🍜", "Chicken noodle soup", "Light broth with chicken and home-style noodles.", 900, 280, H, 3, 35),
            ("soups", "🥣", "Lentil soup", "Red lentils with cumin and lemon.", 850, 290, V | MenuTags.GlutenFree, 3, 30),
            ("salads", "🍅", "Achichuk", "Tomato and onion salad, the classic partner of plov.", 500, 70, V | MenuTags.GlutenFree, 2, 50),
            ("salads", "🥗", "Olivier", "Potato salad with chicken, egg, pickles and peas.", 800, 320, H, 2, 30),
            ("salads", "🥗", "Greek salad", "Cucumber, tomato, pepper, olives and feta.", 950, 210, V | MenuTags.GlutenFree, 3, 25),
            ("salads", "🥕", "Korean carrot salad", "Spicy marinated carrots with garlic and coriander.", 550, 150, V | MenuTags.Spicy, 2, 30),
            ("pastry", "🥟", "Samsa with beef", "Flaky baked pastry filled with minced beef and onion.", 450, 340, H, 1, 60),
            ("pastry", "🍩", "Baursak, 5 pcs", "Golden fried dough, best with tea.", 300, 290, V, 1, 80),
            ("pastry", "🫓", "Belyash", "Fried bun with a juicy beef filling.", 400, 310, H, 1, 40),
            ("pastry", "🧀", "Cheese pie", "Warm pastry with salty cheese.", 600, 360, V, 2, 25),
            ("desserts", "🍯", "Chak-chak", "Crispy dough pieces glazed with honey.", 500, 380, V, 1, 30),
            ("desserts", "🥞", "Syrniki, 3 pcs", "Cottage cheese pancakes with berry jam.", 800, 420, V, 6, 25),
            ("desserts", "🍰", "Medovik", "Layered honey cake with sour cream filling.", 750, 430, V, 1, 20),
            ("drinks", "🍵", "Black tea with milk", "Strong Kazakh-style tea with milk.", 200, 60, V | MenuTags.GlutenFree, 1, 200),
            ("drinks", "🍵", "Green tea", "A pot of green tea.", 200, 5, V | MenuTags.GlutenFree, 1, 150),
            ("drinks", "☕", "Americano", "Double espresso with hot water.", 700, 10, V | MenuTags.GlutenFree, 2, 80),
            ("drinks", "☕", "Cappuccino", "Espresso with steamed milk foam.", 900, 120, V | MenuTags.GlutenFree, 3, 70),
            ("drinks", "🧃", "Dried fruit compote", "Homemade compote of dried apricots and apples.", 300, 110, V | MenuTags.GlutenFree, 1, 80),
            ("drinks", "🥛", "Ayran", "Cold salted yogurt drink.", 350, 90, V | MenuTags.GlutenFree, 1, 60),
            ("drinks", "💧", "Still water 0.5 L", "Bottled still water.", 250, 0, V | MenuTags.GlutenFree, 1, 100),
        ];

        return [.. menu.Select(m => MenuItem.Create(
            new MenuItemDetails(c[m.Category].Id, m.Name, m.Description, m.Price, m.Emoji, null, m.Calories, m.Tags, m.Prep, m.Stock),
            serviceDay).Value)];
    }

#pragma warning disable CA5394 // Random is fine for demo data: it is not used for security.
    private static List<Order> CreateHistory(
        List<MenuItem> items, List<User> students, CanteenRules rules, TimeZoneInfo zone, DateOnly serviceDay, int historyDays, Random random)
    {
        var orders = new List<Order>();
        for (var offset = historyDays; offset >= 1; offset--)
        {
            var day = serviceDay.AddDays(-offset);
            var slots = SlotPlanner.GenerateSlots(day, rules, zone).ToList();
            var weights = slots.Select(s => SlotWeight(TimeOnly.FromDateTime(s.Start.DateTime))).ToArray();
            var count = day.DayOfWeek switch
            {
                DayOfWeek.Sunday => random.Next(12, 22),
                DayOfWeek.Saturday => random.Next(28, 42),
                _ => random.Next(58, 88),
            };

            var drafts = Enumerable.Range(0, count)
                .Select(_ => (Slot: slots[Pick(weights, random)], Customer: students[random.Next(students.Count)]))
                .Select(x => (x.Slot, x.Customer, PlacedAt: x.Slot.Start.AddMinutes(-random.Next(15, 150))))
                .OrderBy(x => x.PlacedAt)
                .ToList();

            var number = OrderNumberStart;
            foreach (var (slot, customer, placedAt) in drafts)
            {
                var lines = PickLines(items, random);
                var placed = Order.Create(new OrderDraft(
                    ++number, day, customer.Id, customer.DisplayName, slot,
                    (PaymentMethod)random.Next(3), null, lines, placedAt));

                orders.Add(placed.Bind(order => PlayOut(order, lines, slot, customer, rules, random)).Value);
            }
        }

        return orders;
    }

    /// <summary>One historical order's life, as a chain of transitions (each one returns a new order).</summary>
    private static Result<Order> PlayOut(Order order, List<OrderLineDraft> lines, PickupSlot slot, User customer, CanteenRules rules, Random random)
    {
        var roll = random.NextDouble();
        if (roll < 0.04)
        {
            return order.CancelByCustomer(customer.Id, order.CreatedAt.AddMinutes(random.Next(2, 10)));
        }

        var cookingAt = slot.Start.AddMinutes(-random.Next(6, 16));
        if (roll < 0.07)
        {
            return order.StartCooking(cookingAt).Bind(o => o.CancelByKitchen("Ran out of an ingredient", cookingAt.AddMinutes(2)));
        }

        var readyAt = cookingAt.AddMinutes(lines.Max(l => l.Item.PrepMinutes) + random.Next(1, 6));
        var ready = order.StartCooking(cookingAt).Bind(o => o.MarkReady(readyAt));
        return roll < 0.11
            ? ready.Bind(o => o.MarkNoShow(slot.End.AddMinutes(rules.NoShowAfterMinutes)))
            : ready.Bind(o => o.MarkPickedUp(Max(readyAt, slot.Start).AddMinutes(random.Next(1, 13))));
    }

    /// <summary>A few orders in the next slots so the kitchen board and the TV screen are not empty in a demo.</summary>
    private static (List<Order> Orders, List<MenuItem> Items) CreateActiveOrders(
        List<MenuItem> items, List<User> students, CanteenRules rules, TimeZoneInfo zone, DateTimeOffset now, DateOnly serviceDay, Random random)
    {
        var upcoming = SlotPlanner.GenerateSlots(serviceDay, rules, zone)
            .Where(s => s.Start >= SlotPlanner.EarliestPickup(now, rules))
            .Take(3)
            .ToList();
        if (upcoming.Count == 0)
        {
            return ([], items);
        }

        // The first two demo students keep their slots free for their own test orders.
        var customers = students.Skip(2).ToList();
        var plan = new[] { OrderStatus.Ready, OrderStatus.Ready, OrderStatus.Cooking, OrderStatus.Cooking, OrderStatus.Placed, OrderStatus.Placed, OrderStatus.Placed };
        var stock = items.ToDictionary(i => i.Id);
        var orders = new List<Order>();

        for (var i = 0; i < plan.Length; i++)
        {
            var slot = upcoming[Math.Min(i / 3, upcoming.Count - 1)];
            var lines = PickLines(items, random);
            foreach (var line in lines)
            {
                stock[line.Item.Id] = stock[line.Item.Id].Reserve(line.Quantity).GetValueOrDefault(stock[line.Item.Id]);
            }

            var customer = customers[i % customers.Count];
            var placed = Order.Create(new OrderDraft(
                OrderNumberStart + i + 1, serviceDay, customer.Id, customer.DisplayName, slot, PaymentMethod.Card,
                i == 4 ? "No onion, please" : null, lines, now.AddMinutes(-random.Next(5, 40))));

            var cookingAt = now.AddMinutes(-random.Next(3, 9));
            var order = plan[i] switch
            {
                OrderStatus.Cooking => placed.Bind(o => o.StartCooking(cookingAt)),
                OrderStatus.Ready => placed.Bind(o => o.StartCooking(cookingAt)).Bind(o => o.MarkReady(now.AddMinutes(-1))),
                _ => placed,
            };
            orders.Add(order.Value);
        }

        return (orders, [.. stock.Values]);
    }

    private static List<OrderLineDraft> PickLines(List<MenuItem> items, Random random)
    {
        var byCategory = items.GroupBy(i => i.CategoryId).ToList();
        var picked = new List<MenuItem>();

        void Maybe(int group, double chance)
        {
            if (random.NextDouble() < chance)
            {
                var options = byCategory[group].Where(i => !picked.Contains(i)).ToList();
                picked.Add(options[random.Next(options.Count)]);
            }
        }

        Maybe(0, 0.72); // main
        Maybe(1, 0.30); // soup
        Maybe(2, 0.28); // salad
        Maybe(3, 0.30); // pastry
        Maybe(4, 0.12); // dessert
        Maybe(5, 0.62); // drink
        if (picked.Count == 0)
        {
            Maybe(0, 1);
        }

        return [.. picked.Select(i => new OrderLineDraft(i, random.NextDouble() < 0.12 ? 2 : 1))];
    }

    /// <summary>Breaks between classes drive demand: a morning peak, a big lunch peak and an afternoon one.</summary>
    private static double SlotWeight(TimeOnly time)
    {
        static double Peak(TimeOnly t, double centerHour, double width, double height) =>
            height * Math.Exp(-Math.Pow((t.ToTimeSpan().TotalHours - centerHour) / width, 2));

        return 0.15 + Peak(time, 10.0, 0.35, 2.2) + Peak(time, 12.9, 0.6, 5) + Peak(time, 15.4, 0.35, 1.6) + Peak(time, 18.2, 0.5, 0.7);
    }

    private static int Pick(double[] weights, Random random)
    {
        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }
#pragma warning restore CA5394

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
