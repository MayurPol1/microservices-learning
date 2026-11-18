using System.ComponentModel.DataAnnotations;
using Identity.Api.Domain;

namespace Identity.Api.Models;

public record RegisterRequest(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserDto(Guid Id, string Email, string Role)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.Role);
}

/// <summary>What login and register return: the token to send as "Authorization: Bearer …", and who you are.</summary>
public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);
