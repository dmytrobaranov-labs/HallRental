using System.Text;
using HallRental.API.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HallRental.IntegrationTests;

/// <summary>
/// Issues JWTs signed with the key from the test host's configuration,
/// so tests never depend on hardcoded tokens that silently expire or drift from the config.
/// </summary>
public static class TestTokens
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static string Create(IServiceProvider services, string role)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"Section '{JwtOptions.SectionName}' is missing from the test configuration.");

        // The JWT bearer handler validates lifetime against the real clock, so the token must too.
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = $"test-{role.ToLowerInvariant()}",
                ["role"] = role
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        });
    }
}
