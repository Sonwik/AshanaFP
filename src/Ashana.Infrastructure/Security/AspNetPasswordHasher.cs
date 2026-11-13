using Ashana.Application.Abstractions;
using Ashana.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Ashana.Infrastructure.Security;

/// <summary>
/// Adapter around ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>:
/// PBKDF2 with HMAC-SHA512, 100,000 iterations and a random 128-bit salt per password.
/// The application layer only sees <see cref="IPasswordHasher"/> (Dependency Inversion).
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _inner.HashPassword(Subject, password);
    }

    public PasswordCheck Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return PasswordCheck.Failed;
        }

        try
        {
            return _inner.VerifyHashedPassword(Subject, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed; // corrupted hash in storage
        }
    }
}
