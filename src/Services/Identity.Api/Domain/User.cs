namespace Identity.Api.Domain;

/// <summary>
/// A MicroShop account. The Identity service is the only owner of user data: other services never see
/// passwords or the users table. They only see what the JWT tells them (id, email, role).
/// </summary>
public class User
{
    private User() { } // for EF Core

    public User(Guid id, string email, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        Id = id;
        Email = NormalizeEmail(email);
        Role = role;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>Stored lower-case, so "Ada@Example.com" and "ada@example.com" are the same account.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Never the password itself: a salted, slow hash (PBKDF2, from ASP.NET Core's PasswordHasher).
    /// Even someone who steals the table can't read the passwords back.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>"Admin" or "Customer" (BuildingBlocks.Security.Roles).</summary>
    public string Role { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
