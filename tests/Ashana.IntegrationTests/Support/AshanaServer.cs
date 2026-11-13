using Ashana.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ashana.IntegrationTests.Support;

/// <summary>
/// Starts the real Ashana web app (the same composition root as Program.cs) on a random local port,
/// with in-memory demo data, a clock that starts at 10:00 in the canteen and the background worker switched off.
/// One server is shared by all tests of a test class.
/// </summary>
public class AshanaServer : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>10:00 today in the canteen (UTC+5): the menu is open and the demo has active orders.</summary>
    public static DateTimeOffset CanteenMorning
    {
        get
        {
            var offset = TimeSpan.FromHours(5);
            var today = DateOnly.FromDateTime(DateTime.UtcNow + offset);
            return new DateTimeOffset(today.ToDateTime(new TimeOnly(10, 0)), offset);
        }
    }

    public Uri BaseAddress { get; private set; } = null!;

    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("The server is not running.");

    /// <summary>Extra configuration as command-line style arguments, for example <c>--RateLimits:AuthPerMinute=3</c>.</summary>
    protected virtual IEnumerable<string> ExtraArguments => [];

    public async Task InitializeAsync()
    {
        string[] args =
        [
            "--Storage:Provider=Memory",
            "--Seed:DemoData=true",
            "--Worker:Enabled=false",
            "--Canteen:TimeZone=Ashana/Test", // unknown id: fixed UTC+5 fallback, independent of the machine
            "--Canteen:FallbackUtcOffsetHours=5",
            "--RateLimits:AuthPerMinute=1000",
            "--RateLimits:ApiPerMinute=1000",
            "--Logging:LogLevel:Default=Warning",
            .. ExtraArguments,
        ];

        _app = await AshanaApp.BuildAsync(
            new WebApplicationOptions
            {
                Args = args,
                ApplicationName = "Ashana.Web",
                ContentRootPath = FindWebProject(),
                EnvironmentName = Environments.Development,
            },
            builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddSingleton<TimeProvider>(new ShiftedTimeProvider(CanteenMorning));
            });

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        BaseAddress = new Uri(address);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>A new "browser": its own cookie jar, no automatic redirects (so tests can check them).</summary>
    public TestBrowser CreateBrowser() => new(BaseAddress);

    private static string FindWebProject()
    {
        // Lets a CI job (or a test runner outside the repository) point at the web project explicitly.
        if (Environment.GetEnvironmentVariable("ASHANA_WEB_ROOT") is { Length: > 0 } configured)
        {
            return configured;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Ashana.Web");
            if (File.Exists(Path.Combine(candidate, "Ashana.Web.csproj")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find src/Ashana.Web above " + AppContext.BaseDirectory);
    }

    /// <summary>The real clock moved so that the test starts at <paramref name="start"/>; time keeps running.</summary>
    private sealed class ShiftedTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly TimeSpan _shift = start - TimeProvider.System.GetUtcNow();

        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + _shift;
    }
}

/// <summary>The same app with a tight sign-in limit, to prove the rate limiter works.</summary>
public sealed class StrictRateLimitServer : AshanaServer
{
    protected override IEnumerable<string> ExtraArguments => ["--RateLimits:AuthPerMinute=3"];
}
