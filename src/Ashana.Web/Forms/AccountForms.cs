using System.ComponentModel.DataAnnotations;

namespace Ashana.Web.Forms;

/// <summary>
/// Form models (view models) for Blazor's EditForm. DataAnnotations give instant field-level feedback;
/// the domain still validates everything again on the server, so the form is a convenience, not the gatekeeper.
/// </summary>
public sealed class LoginInput
{
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;
}

public sealed class RegisterInput
{
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(50, ErrorMessage = "Name must be at most 50 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repeat the password.")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
