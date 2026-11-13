using Ashana.Domain.Users;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public sealed class AccountServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task New_account_is_a_student_with_a_hashed_password()
    {
        var created = await _app.Accounts.RegisterAsync(" New.Student@Uni.KZ ", "Dana Serikova", "Secret2026");

        var stored = await _app.UserAsync(created.Value.Id);
        Assert.Equal(("new.student@uni.kz", UserRole.Student), (created.Value.Email, created.Value.Role));
        Assert.DoesNotContain("Secret2026", stored.PasswordHash, StringComparison.Ordinal);
        Assert.True((await _app.Accounts.AuthenticateAsync("NEW.student@uni.kz", "Secret2026")).IsSuccess);
    }

    [Fact]
    public async Task Email_can_only_be_registered_once()
    {
        var result = await _app.Accounts.RegisterAsync("STUDENT@test.kz", "Someone", "Secret2026");

        Assert.Equal(UserErrors.EmailTaken, result.Error);
    }

    [Fact]
    public async Task Weak_passwords_are_refused() =>
        Assert.Equal(UserErrors.PasswordTooWeak, (await _app.Accounts.RegisterAsync("a@uni.kz", "Name", "password")).Error);

    [Fact]
    public async Task Unknown_email_and_wrong_password_give_the_same_answer()
    {
        var unknown = await _app.Accounts.AuthenticateAsync("nobody@test.kz", TestApp.Password);
        var wrong = await _app.Accounts.AuthenticateAsync("student@test.kz", "Wrong1234");

        Assert.Equal(UserErrors.InvalidCredentials, unknown.Error);
        Assert.Equal(UserErrors.InvalidCredentials, wrong.Error); // nobody can probe which emails exist
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_five_minutes()
    {
        for (var i = 0; i < 4; i++)
        {
            await _app.Accounts.AuthenticateAsync("student@test.kz", "Wrong1234");
        }

        var fifth = await _app.Accounts.AuthenticateAsync("student@test.kz", "Wrong1234");
        var rightButLocked = await _app.Accounts.AuthenticateAsync("student@test.kz", TestApp.Password);
        _app.Time.Advance(TimeSpan.FromMinutes(5));
        var later = await _app.Accounts.AuthenticateAsync("student@test.kz", TestApp.Password);

        Assert.Equal(UserErrors.LockedOut, fifth.Error);
        Assert.Equal(UserErrors.LockedOut, rightButLocked.Error);
        Assert.True(later.IsSuccess);
    }

    [Fact]
    public async Task Blocked_accounts_cannot_sign_in()
    {
        await _app.UserAdmin.SetActiveAsync(_data.AdminId, _data.StudentId, isActive: false);

        Assert.Equal(UserErrors.Inactive, (await _app.Accounts.AuthenticateAsync("student@test.kz", TestApp.Password)).Error);
    }

    [Fact]
    public async Task Successful_sign_in_is_recorded()
    {
        var result = await _app.Accounts.AuthenticateAsync("cook@test.kz", TestApp.Password);

        Assert.Equal(UserRole.Cook, result.Value.Role);
        Assert.Equal(TestData.Now, (await _app.UserAsync(_data.CookId)).LastLoginAt);
    }
}

public sealed class UserAdminServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Admin_can_make_a_student_a_cook()
    {
        var result = await _app.UserAdmin.ChangeRoleAsync(_data.AdminId, _data.Student2Id, UserRole.Cook);

        Assert.Equal(UserRole.Cook, result.Value.Role);
        Assert.Equal(UserRole.Cook, (await _app.UserAsync(_data.Student2Id)).Role);
    }

    [Fact]
    public async Task Admins_cannot_change_or_block_themselves()
    {
        Assert.Equal(UserErrors.CannotChangeSelf, (await _app.UserAdmin.ChangeRoleAsync(_data.AdminId, _data.AdminId, UserRole.Student)).Error);
        Assert.Equal(UserErrors.CannotChangeSelf, (await _app.UserAdmin.SetActiveAsync(_data.AdminId, _data.AdminId, false)).Error);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted_or_blocked()
    {
        var secondAdmin = await _app.AddUserAsync("boss@test.kz", UserRole.Admin, "Second Admin");
        var blockSecond = await _app.UserAdmin.SetActiveAsync(_data.AdminId, secondAdmin, false); // fine: two active admins

        var demote = await _app.UserAdmin.ChangeRoleAsync(secondAdmin, _data.AdminId, UserRole.Student);
        var block = await _app.UserAdmin.SetActiveAsync(secondAdmin, _data.AdminId, false);

        Assert.True(blockSecond.IsSuccess);
        Assert.Equal(UserErrors.LastAdmin, demote.Error);
        Assert.Equal(UserErrors.LastAdmin, block.Error);
    }

    [Fact]
    public async Task List_shows_admins_first()
    {
        var users = await _app.UserAdmin.ListAsync();

        Assert.Equal(UserRole.Admin, users[0].Role);
        Assert.Equal(UserRole.Cook, users[1].Role);
        Assert.Equal(4, users.Count);
    }
}
