using BuildingBlocks.Security;
using Identity.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security;

/// <summary>
/// Creates the JWT a user receives after logging in. A JWT is three Base64 parts, "header.payload.signature":
/// the payload holds the claims below (readable by anyone, so never put secrets in it), and the signature
/// proves that the Identity service issued it and nobody changed a claim, such as role=Customer → Admin.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public IssuedToken CreateToken(User user)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.UserId] = user.Id.ToString(),
                [ClaimNames.Email] = user.Email,
                [ClaimNames.Role] = user.Role,
            },
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);
