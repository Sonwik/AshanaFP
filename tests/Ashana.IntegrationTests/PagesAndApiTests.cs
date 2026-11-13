using System.Net;
using System.Text.Json;
using Ashana.IntegrationTests.Support;

namespace Ashana.IntegrationTests;

/// <summary>What anyone (not signed in) gets from the server: pages, the public API, error pages and headers.</summary>
public sealed class PagesAndApiTests(AshanaServer server) : IClassFixture<AshanaServer>, IDisposable
{
    private readonly TestBrowser _browser = server.CreateBrowser();

    public void Dispose() => _browser.Dispose();

    [Fact]
    public async Task Health_check_reports_healthy()
    {
        Assert.Equal("Healthy", await _browser.GetStringAsync("/health"));
    }

    [Fact]
    public async Task Menu_page_is_rendered_on_the_server_with_the_dishes()
    {
        var html = await _browser.GetStringAsync("/");

        Assert.Contains("Beshbarmak", html);
        Assert.Contains("<title>Menu | Ashana</title>", html);
        Assert.Contains("lang=\"en\"", html);
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/board")]
    [InlineData("/account/login")]
    [InlineData("/account/register")]
    [InlineData("/offline.html")]
    [InlineData("/manifest.webmanifest")]
    public async Task Public_pages_open(string url)
    {
        using var response = await _browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/orders")]
    [InlineData("/checkout")]
    [InlineData("/kitchen")]
    [InlineData("/admin/menu")]
    public async Task Private_pages_send_visitors_to_sign_in_and_back(string url)
    {
        using var response = await _browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/account/login?ReturnUrl={Uri.EscapeDataString(url)}", _browser.RedirectPath(response));
    }

    [Fact]
    public async Task Unknown_pages_get_a_friendly_404_page()
    {
        using var response = await _browser.GetAsync("/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/no-such-endpoint", HttpStatusCode.NotFound)]
    [InlineData("/api/orders/mine", HttpStatusCode.Unauthorized)]
    [InlineData("/api/admin/orders.csv", HttpStatusCode.Unauthorized)]
    public async Task Api_errors_are_plain_status_codes_not_html_pages(string url, HttpStatusCode expected)
    {
        using var response = await _browser.GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location); // never a redirect to the sign-in page
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Menu_api_returns_categories_and_dishes()
    {
        using var json = JsonDocument.Parse(await _browser.GetStringAsync("/api/menu"));

        var categories = json.RootElement.GetProperty("categories");
        var items = json.RootElement.GetProperty("items");
        Assert.Equal(6, categories.GetArrayLength());
        Assert.Contains(items.EnumerateArray(), i => i.GetProperty("name").GetString() == "Plov" && i.GetProperty("price").GetDecimal() > 0);
    }

    [Fact]
    public async Task Slots_api_lists_bookable_pickup_times()
    {
        using var json = JsonDocument.Parse(await _browser.GetStringAsync("/api/slots"));

        var day = json.RootElement.GetProperty("days")[0];
        Assert.Equal("Today", day.GetProperty("title").GetString());
        Assert.Contains(day.GetProperty("slots").EnumerateArray(), s => s.GetProperty("isBookable").GetBoolean());
        Assert.Equal("Available", json.RootElement.GetProperty("firstBookable").GetProperty("state").GetString()); // enums as text
    }

    [Fact]
    public async Task Pickup_board_shows_numbers_but_never_names()
    {
        var body = await _browser.GetStringAsync("/api/board");
        using var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.GetProperty("preparing").GetArrayLength() + json.RootElement.GetProperty("ready").GetArrayLength() > 0);
        Assert.DoesNotContain("customer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Madina", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_carry_security_headers()
    {
        using var response = await _browser.GetAsync("/");

        var csp = string.Join(';', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }

    [Fact]
    public async Task Component_library_styles_are_served()
    {
        using var response = await _browser.GetAsync("/_content/Ashana.UI/ashana-ui.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Sign_out_cannot_be_triggered_by_another_site()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "/" });

        using var response = await _browser.Http.PostAsync(new Uri("/account/logout", UriKind.Relative), form); // no antiforgery token

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
