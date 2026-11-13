using Ashana.Domain.Menu;
using Ashana.Domain.Users;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Domain;

public class CategoryTests
{
    [Fact]
    public void Create_trims_the_name_and_uses_a_default_icon()
    {
        var category = Category.Create("  Soups ", " ", 3).Value;

        Assert.Equal("Soups", category.Name);
        Assert.Equal("🍽️", category.Icon);
        Assert.Equal(3, category.SortOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A category name that is far too long")]
    public void Create_rejects_blank_or_long_names(string name) =>
        Assert.Equal("menu.category_name", Category.Create(name, "🍲", 1).Error.Code);

    [Fact]
    public void Rename_bumps_the_version()
    {
        var category = Category.Create("Soups", "🍲", 1).Value;

        var renamed = category.Rename("Hot soups", "🍜", 2).Value;

        Assert.Equal(("Hot soups", "🍜", 2, 1), (renamed.Name, renamed.Icon, renamed.SortOrder, renamed.Version));
        Assert.Equal("Soups", category.Name);
    }
}

public class UserTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Email_is_stored_trimmed_and_lower_case()
    {
        var user = User.Create("  Student@Ashana.TEST ", " Aruzhan ", "hash", UserRole.Student, Now).Value;

        Assert.Equal("student@ashana.test", user.Email);
        Assert.Equal("Aruzhan", user.DisplayName);
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign.kz")]
    [InlineData("name@localhost")]
    [InlineData("two@@signs.kz")]
    [InlineData("with space@uni.kz")]
    public void Invalid_emails_are_rejected(string email) =>
        Assert.Equal(UserErrors.InvalidEmail, User.Create(email, "Name", "hash", UserRole.Student, Now).Error);

    [Fact]
    public void Name_is_required_and_limited()
    {
        Assert.Equal(UserErrors.NameRequired, User.Create("a@uni.kz", " ", "hash", UserRole.Student, Now).Error);
        Assert.Equal(UserErrors.NameTooLong, User.Create("a@uni.kz", new string('n', 51), "hash", UserRole.Student, Now).Error);
    }

    [Theory]
    [InlineData("Ashana2026", true)]
    [InlineData("abc12345", true)]
    [InlineData("short1", false)]       // fewer than 8 characters
    [InlineData("onlyletters", false)]  // no digit
    [InlineData("1234567890", false)]   // no letter
    public void Password_needs_eight_characters_with_a_letter_and_a_digit(string password, bool valid) =>
        Assert.Equal(valid, User.ValidatePassword(password).IsSuccess);

    [Fact]
    public void Very_long_passwords_are_rejected() =>
        Assert.Equal(UserErrors.PasswordTooLong, User.ValidatePassword("a1" + new string('x', 127)).Error);

    private static User NewUser() => User.Create("a@uni.kz", "Name", "hash", UserRole.Student, Now).Value;

    /// <summary>Folds n wrong passwords over the user, like replaying n failed sign-ins.</summary>
    private static User AfterWrongPasswords(User user, int count) =>
        Enumerable.Range(0, count).Aggregate(user, (u, _) => u.Login(PasswordCheck.Failed, Now, 5, TimeSpan.FromMinutes(5)) switch
        {
            LoginOutcome.WrongPassword wrong => wrong.User,
            _ => u,
        });

    [Fact]
    public void Account_locks_after_the_fifth_wrong_password_and_unlocks_later()
    {
        var afterFour = AfterWrongPasswords(NewUser(), 4);
        Assert.False(afterFour.IsLockedOut(Now));

        var fifth = Assert.IsType<LoginOutcome.WrongPassword>(afterFour.Login(PasswordCheck.Failed, Now, 5, TimeSpan.FromMinutes(5)));

        Assert.True(fifth.NowLocked);
        Assert.True(fifth.User.IsLockedOut(Now.AddMinutes(4)));
        Assert.False(fifth.User.IsLockedOut(Now.AddMinutes(5)));
    }

    [Fact]
    public void A_locked_account_is_refused_even_with_the_right_password() =>
        Assert.IsType<LoginOutcome.LockedOut>(AfterWrongPasswords(NewUser(), 5).Login(PasswordCheck.Success, Now.AddMinutes(1), 5, TimeSpan.FromMinutes(5)));

    [Fact]
    public void Successful_sign_in_clears_failed_attempts()
    {
        var user = AfterWrongPasswords(NewUser(), 2);

        var signedIn = Assert.IsType<LoginOutcome.SignedIn>(user.Login(PasswordCheck.Success, Now.AddMinutes(1), 5, TimeSpan.FromMinutes(5)));

        Assert.Equal(0, signedIn.User.FailedLoginCount);
        Assert.Equal(Now.AddMinutes(1), signedIn.User.LastLoginAt);
        Assert.False(signedIn.RehashNeeded);
        Assert.Equal(2, user.FailedLoginCount); // the old value is unchanged
    }

    [Fact]
    public void A_blocked_account_with_the_right_password_is_its_own_case() =>
        Assert.IsType<LoginOutcome.Blocked>(NewUser().SetActive(false).Login(PasswordCheck.SuccessRehashNeeded, Now, 5, TimeSpan.FromMinutes(5)));

    [Fact]
    public void An_old_hash_asks_for_a_rehash() =>
        Assert.True(Assert.IsType<LoginOutcome.SignedIn>(NewUser().Login(PasswordCheck.SuccessRehashNeeded, Now, 5, TimeSpan.FromMinutes(5))).RehashNeeded);

    [Fact]
    public void Role_and_block_changes_are_versioned()
    {
        var user = NewUser().ChangeRole(UserRole.Cook).SetActive(false);

        Assert.Equal((UserRole.Cook, false, 2), (user.Role, user.IsActive, user.Version));
    }
}
