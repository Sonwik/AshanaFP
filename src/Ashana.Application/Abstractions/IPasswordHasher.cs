using Ashana.Domain.Users;

namespace Ashana.Application.Abstractions;

/// <summary>
/// Hashes passwords (a slow, side-effecting operation). Implemented in Infrastructure with ASP.NET Core's PBKDF2 hasher.
/// The domain never calls it: it only receives the <see cref="PasswordCheck"/> answer.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string hash, string password);
}
