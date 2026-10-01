namespace ProvisioningGateway.Api.Security;

// Authentifie les appels entrants (midPoint -> Gateway) via une cle API
// dans le header X-Api-Key. Volontairement simple pour ce PoC ; en
// production on pourrait passer sur un schema d'authentification
// ASP.NET Core complet (AddAuthentication) si plusieurs clients/cles
// doivent etre distingues.
public class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly string _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _expectedKey = configuration["Gateway:ApiKey"]
            ?? throw new InvalidOperationException("Gateway:ApiKey manquant dans la configuration.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            providedKey != _expectedKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Cle API manquante ou invalide." });
            return;
        }

        await _next(context);
    }
}
