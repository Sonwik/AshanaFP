using System.Net;
using System.Text.RegularExpressions;
using Ashana.Infrastructure.Seeding;

namespace Ashana.IntegrationTests.Support;

/// <summary>
/// A minimal "browser" for server-rendered pages: keeps cookies, reads the antiforgery token from a page
/// and posts forms the way a real browser would (without JavaScript).
/// </summary>
public sealed partial class TestBrowser : IDisposable
{
    private readonly HttpClientHandler _handler;

    public TestBrowser(Uri baseAddress)
    {
        _handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false, UseCookies = true };
        Http = new HttpClient(_handler) { BaseAddress = baseAddress };
    }

    public HttpClient Http { get; }

    public bool HasAuthCookie => _handler.CookieContainer.GetCookies(Http.BaseAddress!).Any(c => c.Name == "ashana.auth");

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(new Uri(url, UriKind.Relative));

    public async Task<string> GetStringAsync(string url)
    {
        using var response = await GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Opens <paramref name="pageUrl"/>, then submits the form named <paramref name="formName"/> with the given fields.</summary>
    public async Task<HttpResponseMessage> SubmitFormAsync(string pageUrl, string formName, IDictionary<string, string>? fields = null, string? postUrl = null)
    {
        var html = await GetStringAsync(pageUrl);
        var token = AntiforgeryToken(html) ?? throw new InvalidOperationException($"No antiforgery token on {pageUrl}");
        var values = new Dictionary<string, string>(fields ?? new Dictionary<string, string>())
        {
            ["_handler"] = formName,
            ["__RequestVerificationToken"] = token,
        };

        using var content = new FormUrlEncodedContent(values);
        return await Http.PostAsync(new Uri(postUrl ?? pageUrl, UriKind.Relative), content);
    }

    public Task<HttpResponseMessage> SignInAsync(string email, string password = DemoAccounts.Password, string page = "/account/login") =>
        SubmitFormAsync(page, "login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "true",
        });

    /// <summary>Signs in and fails the test if it did not work.</summary>
    public async Task SignInOrFailAsync(string email)
    {
        using var response = await SignInAsync(email);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(HasAuthCookie, "No sign-in cookie was set");
    }

    /// <summary>Path and query of a redirect target, whether the server sent an absolute or a relative URL.</summary>
    public string RedirectPath(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var location = response.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect, got {(int)response.StatusCode}");
        return (location.IsAbsoluteUri ? location : new Uri(Http.BaseAddress!, location)).PathAndQuery;
    }

    public static string? AntiforgeryToken(string html) =>
        TokenPattern().Match(html) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;

    public void Dispose()
    {
        Http.Dispose();
        _handler.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
