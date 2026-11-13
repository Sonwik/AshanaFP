using System.Net;
using System.Text;
using Ashana.Infrastructure.Seeding;
using Ashana.IntegrationTests.Support;

namespace Ashana.IntegrationTests;

/// <summary>Sign-in, registration, sign-out and role-based access, over real HTTP with cookies.</summary>
public sealed class AuthTests(AshanaServer server) : IClassFixture<AshanaServer>
{
    [Fact]
    public async Task Student_signs_in_and_is_sent_back_to_the_page_they_wanted()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SignInAsync(DemoAccounts.StudentEmail, page: "/account/login?ReturnUrl=%2Forders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/orders", browser.RedirectPath(response));
        Assert.True(browser.HasAuthCookie);
        Assert.Contains("My orders", await browser.GetStringAsync("/orders"));
    }

    [Theory]
    [InlineData(DemoAccounts.StudentEmail, "/")]
    [InlineData(DemoAccounts.CookEmail, "/kitchen")]
    [InlineData(DemoAccounts.AdminEmail, "/admin")]
    public async Task Each_role_lands_on_its_own_start_page(string email, string expectedPage)
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SignInAsync(email);

        Assert.Equal(expectedPage, browser.RedirectPath(response));
    }

    [Fact]
    public async Task Wrong_password_shows_an_error_and_sets_no_cookie()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SignInAsync(DemoAccounts.SecondStudentEmail, "Wrong-password-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Wrong email or password.", await response.Content.ReadAsStringAsync());
        Assert.False(browser.HasAuthCookie);
    }

    [Fact]
    public async Task Return_address_on_another_site_is_ignored()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SignInAsync(DemoAccounts.StudentEmail, page: "/account/login?ReturnUrl=https%3A%2F%2Fevil.example%2F");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", browser.RedirectPath(response));
    }

    [Fact]
    public async Task One_click_demo_sign_in_works_without_typing()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SubmitFormAsync("/account/login", "demo-cook");

        Assert.Equal("/kitchen", browser.RedirectPath(response));
        Assert.True(browser.HasAuthCookie);
    }

    [Fact]
    public async Task New_student_registers_and_is_signed_in()
    {
        using var browser = server.CreateBrowser();
        var email = $"new-{Guid.NewGuid():N}@uni.kz";

        using var response = await browser.SubmitFormAsync("/account/register", "register", new Dictionary<string, string>
        {
            ["Input.Name"] = "Dana Serikova",
            ["Input.Email"] = email,
            ["Input.Password"] = "Secret2026",
            ["Input.ConfirmPassword"] = "Secret2026",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(browser.HasAuthCookie);
        Assert.Equal("[]", await browser.GetStringAsync("/api/orders/mine")); // a fresh account has no orders
    }

    [Fact]
    public async Task Registration_validates_the_form_on_the_server()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.SubmitFormAsync("/account/register", "register", new Dictionary<string, string>
        {
            ["Input.Name"] = "Dana",
            ["Input.Email"] = "not-an-email",
            ["Input.Password"] = "Secret2026",
            ["Input.ConfirmPassword"] = "Different2026",
        });

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a valid email address.", html);
        Assert.Contains("The passwords do not match.", html);
        Assert.False(browser.HasAuthCookie);
    }

    [Fact]
    public async Task Students_cannot_open_kitchen_or_admin_pages()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.StudentEmail);

        using var kitchen = await browser.GetAsync("/kitchen");
        using var csv = await browser.GetAsync("/api/admin/orders.csv");

        Assert.Equal(HttpStatusCode.Redirect, kitchen.StatusCode);
        Assert.StartsWith("/account/denied", browser.RedirectPath(kitchen), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, csv.StatusCode);
    }

    [Fact]
    public async Task Cook_sees_the_live_kitchen_board()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.CookEmail);

        var html = await browser.GetStringAsync("/kitchen");

        Assert.Contains("Start cooking", html);
        Assert.Contains("data-order=", html);
    }

    [Fact]
    public async Task Cook_cannot_open_admin_pages()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.CookEmail);

        using var response = await browser.GetAsync("/admin/users");

        Assert.StartsWith("/account/denied", browser.RedirectPath(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_downloads_orders_as_a_csv_file_that_excel_reads()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.AdminEmail);

        using var response = await browser.GetAsync("/api/admin/orders.csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 byte order mark
        Assert.StartsWith("﻿Date,Number,Slot,Customer", text, StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length > 100);
    }

    [Fact]
    public async Task Signed_in_user_reads_their_own_orders_only()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.SecondStudentEmail);

        using var someoneElses = await browser.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.StartsWith("[", await browser.GetStringAsync("/api/orders/mine"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
        Assert.Equal("application/problem+json", someoneElses.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Sign_out_with_the_form_clears_the_session()
    {
        using var browser = server.CreateBrowser();
        await browser.SignInOrFailAsync(DemoAccounts.StudentEmail);

        using var response = await browser.SubmitFormAsync("/account", "logout", new Dictionary<string, string> { ["returnUrl"] = "/" }, postUrl: "/account/logout");
        using var after = await browser.GetAsync("/api/orders/mine");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(browser.HasAuthCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}

/// <summary>Password guessing is slowed down per IP address.</summary>
public sealed class RateLimitTests(StrictRateLimitServer server) : IClassFixture<StrictRateLimitServer>
{
    [Fact]
    public async Task Too_many_sign_in_attempts_get_429()
    {
        using var browser = server.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var response = await browser.SignInAsync(DemoAccounts.SecondStudentEmail, "Wrong-password-1");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
