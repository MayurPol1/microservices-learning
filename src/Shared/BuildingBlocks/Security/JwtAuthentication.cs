using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Security;

/// <summary>The roles MicroShop knows. They travel inside the JWT as the "role" claim.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}

/// <summary>JWT claim names used across MicroShop (standard JWT names, not .NET's long URIs).</summary>
public static class ClaimNames
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Role = "role";
}

/// <summary>Bound from the "Jwt" configuration section of every service.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Who issued the token: the Identity service.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Who the token is for: the MicroShop APIs.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 key, at least 32 characters. Every service that validates tokens needs the same key.
    /// That's the simple option; its weakness is that any service holding the key could also *create*
    /// tokens. Production systems usually sign with a private key (RS256) that only the identity
    /// service holds, and publish the public key for validation (README, next exercises).
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeMinutes { get; set; } = 120;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Validates "Authorization: Bearer &lt;jwt&gt;" on every request: signature, issuer, audience, expiry.
    /// A valid token becomes HttpContext.User with its claims, which [Authorize(Roles = ...)] and the
    /// ownership checks in controllers then use.
    /// </summary>
    public static IHostApplicationBuilder AddJwtAuthentication(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(JwtOptions.SectionName);
        builder.Services.Configure<JwtOptions>(section);

        var jwt = section.Get<JwtOptions>()
            ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
        if (jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters for HS256.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim names as they are in the token ("sub", "role") instead of mapping them
                // to .NET's long schema URIs, so the code reads the same names the token contains.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidateIssuerSigningKey = true,
                    NameClaimType = ClaimNames.Email,
                    RoleClaimType = ClaimNames.Role, // makes User.IsInRole and [Authorize(Roles=...)] work
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddAuthorization();
        return builder;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimNames.UserId), out var id)
            ? id
            : throw new InvalidOperationException("The token has no valid 'sub' claim.");

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimNames.Email) ?? throw new InvalidOperationException("The token has no 'email' claim.");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);
}
