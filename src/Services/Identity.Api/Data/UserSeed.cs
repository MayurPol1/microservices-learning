using BuildingBlocks.Security;
using Identity.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Data;

/// <summary>
/// Demo accounts for the learning project (listed in the README). There's no other way to become an
/// admin: registration always creates customers. A real system would create the first admin from a
/// deployment secret, not from source code.
/// </summary>
public static class UserSeed
{
    public const string AdminEmail = "admin@microshop.local";
    public const string AdminPassword = "Admin123!";
    public const string CustomerEmail = "customer@microshop.local";
    public const string CustomerPassword = "Customer123!";

    public static async Task SeedAsync(UsersDbContext db, IPasswordHasher<User> hasher)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        db.Users.AddRange(
            Create(AdminEmail, AdminPassword, Roles.Admin, hasher),
            Create(CustomerEmail, CustomerPassword, Roles.Customer, hasher));
        await db.SaveChangesAsync();
    }

    private static User Create(string email, string password, string role, IPasswordHasher<User> hasher)
    {
        var user = new User(Guid.NewGuid(), email, role);
        user.SetPasswordHash(hasher.HashPassword(user, password));
        return user;
    }
}
