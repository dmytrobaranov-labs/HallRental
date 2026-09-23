using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HallRental.API.Auth;

/// <summary>
/// ДЕМО-ендпоінт для видачі тестових токенів у хмарі (замість токенів, захардкоджених у README).
/// Вмикається лише налаштуванням Auth:DemoTokenEndpointEnabled=true.
/// УВАГА: будь-хто, хто знає адресу, зможе отримати токен адміністратора, поки ендпоінт увімкнено —
/// тримайте його вимкненим, окрім моментів демонстрації. У реальному продукті — Microsoft Entra ID.
/// </summary>
public static class DemoTokenEndpoint
{
    public static void MapDemoTokenEndpoint(this WebApplication app, JwtOptions jwt)
    {
        if (!app.Configuration.GetValue<bool>("Auth:DemoTokenEndpointEnabled"))
            return;

        app.MapPost("/api/v1/auth/demo-token", (string role) =>
        {
            if (role is not ("Admin" or "User"))
                return Results.BadRequest(new { error = "role має бути Admin або User" });

            var handler = new JsonWebTokenHandler();
            var token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = jwt.Issuer,
                Audience = jwt.Audience,
                Expires = DateTime.UtcNow.AddHours(1),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = $"demo-{role.ToLowerInvariant()}",
                    ["name"] = $"Demo {role}",
                    ["role"] = role
                },
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    SecurityAlgorithms.HmacSha256)
            });

            return Results.Ok(new { token, expiresInSeconds = 3600 });
        })
        .AllowAnonymous()
        .WithSummary("ДЕМО: отримати тестовий JWT")
        .WithDescription("Лише для демонстрації. Вимкнено за замовчуванням.");
    }
}
