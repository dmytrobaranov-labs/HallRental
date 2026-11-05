using HallRental.API.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HallRental.IntegrationTests;

/// <summary>
/// Hosts the API in the Production environment, where appsettings.Development.json is not loaded
/// and Minimal API binding failures produce empty 400s instead of exceptions (ThrowOnBadRequest is off).
/// </summary>
public sealed class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    // Test-only key; Production reads the real one from Key Vault.
    private const string TestSigningKey = "production-test-signing-key-not-a-secret-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        // UseSetting (not ConfigureAppConfiguration) so the key is visible while Program.cs is still
        // building the host: it reads Jwt:SigningKey before builder.Build().
        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}", TestSigningKey);
    }
}
