using BuildingBlocks.Security;
using Identity.Api.Data;
using Identity.Api.Domain;
using Identity.Api.Models;
using Identity.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    UsersDbContext db,
    IPasswordHasher<User> hasher,
    TokenService tokens,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Creates a Customer account and logs it in. Admins can't be created this way.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        // "Domain.User", because inside a controller plain "User" means the logged-in ClaimsPrincipal.
        var email = Domain.User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered",
                detail: "An account with this email already exists. Log in instead.");
        }

        var user = new User(Guid.NewGuid(), email, Roles.Customer);
        user.SetPasswordHash(hasher.HashPassword(user, request.Password));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Registered customer {UserId}", user.Id);
        return ToResponse(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = Domain.User.NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        var result = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            // Same answer for "no such user" and "wrong password", so the API doesn't reveal which emails exist.
            logger.LogInformation("Failed login attempt");
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login failed",
                detail: "Invalid email or password.");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hashing algorithm got stronger since this hash was made: upgrade it now that we know the password.
            user!.SetPasswordHash(hasher.HashPassword(user, request.Password));
            await db.SaveChangesAsync(ct);
        }

        return ToResponse(user!);
    }

    /// <summary>Who am I? Reads the user id from the token, then the account from the database.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? Unauthorized() : UserDto.From(user);
    }

    private AuthResponse ToResponse(User user)
    {
        var token = tokens.CreateToken(user);
        return new AuthResponse(token.AccessToken, token.ExpiresAtUtc, UserDto.From(user));
    }
}
