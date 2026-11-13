namespace Ashana.Domain.Users;

public static class UserErrors
{
    public static readonly Error InvalidEmail = Error.Validation("user.email", "Enter a valid email address.");
    public static readonly Error NameRequired = Error.Validation("user.name_required", "Name is required.");
    public static readonly Error NameTooLong = Error.Validation("user.name_too_long", $"Name must be at most {User.NameMaxLength} characters.");
    public static readonly Error PasswordTooWeak = Error.Validation("user.password_weak", $"Password must be at least {User.MinPasswordLength} characters and contain a letter and a digit.");
    public static readonly Error PasswordTooLong = Error.Validation("user.password_long", "Password must be at most 128 characters.");
    public static readonly Error EmailTaken = Error.Conflict("user.email_taken", "An account with this email already exists.");

    /// <summary>Deliberately vague: we do not reveal whether the email exists.</summary>
    public static readonly Error InvalidCredentials = Error.Validation("user.credentials", "Wrong email or password.");
    public static readonly Error LockedOut = Error.Forbidden("user.locked", "Too many failed attempts. Try again in a few minutes.");
    public static readonly Error Inactive = Error.Forbidden("user.inactive", "This account is blocked. Contact the canteen administrator.");
    public static readonly Error NotFound = Error.NotFound("user.not_found", "User not found.");
    public static readonly Error CannotChangeSelf = Error.Forbidden("user.self", "You cannot change your own role or block yourself.");
    public static readonly Error LastAdmin = Error.Conflict("user.last_admin", "At least one active administrator must remain.");
}
