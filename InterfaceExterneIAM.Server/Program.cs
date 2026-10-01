using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using InterfaceExterneIAM.Server.Dtos;
using InterfaceExterneIAM.Server.Exceptions;
using InterfaceExterneIAM.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient("Midpoint", client =>
{
    client.BaseAddress = new Uri("http://localhost:8081/midpoint/ws/rest/");
});
builder.Services.AddScoped<MidpointService>();

const string BlazorClientCorsPolicy = "BlazorClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(BlazorClientCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5179", "https://localhost:7204")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Le cookie de session traverse deux origines (Blazor <-> BFF), donc SameSite=None + Secure.
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // Sans session valide, le comportement par defaut redirige vers Keycloak (challenge OIDC) meme
    // pour les appels fetch/XHR de Blazor vers /api/* : le navigateur bloque cette redirection
    // cross-origin avec une erreur CORS au lieu d'un 401 exploitable. Pour les routes API, on
    // renvoie donc explicitement 401/403 en JSON plutot que de rediriger.
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Keycloak:Authority"];
    options.ClientId = builder.Configuration["Keycloak:ClientId"];
    options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Add("profile");
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.CorrelationCookie.SameSite = SameSiteMode.None;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    options.NonceCookie.SameSite = SameSiteMode.None;
    options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
    // Keycloak tourne en http://localhost:8080 en dev : la découverte OIDC ne doit pas exiger HTTPS.
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(BlazorClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/bff/login", (string? returnUrl, IConfiguration configuration) =>
{
    var blazorOrigin = configuration["BlazorClient:Origin"] ?? string.Empty;
    var redirectTo = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith(blazorOrigin, StringComparison.OrdinalIgnoreCase)
        ? returnUrl
        : blazorOrigin;

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = redirectTo },
        new[] { OpenIdConnectDefaults.AuthenticationScheme });
})
.WithName("BffLogin");

app.MapGet("/bff/logout", (string? returnUrl, IConfiguration configuration) =>
{
    var blazorOrigin = configuration["BlazorClient:Origin"] ?? string.Empty;
    var redirectTo = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith(blazorOrigin, StringComparison.OrdinalIgnoreCase)
        ? returnUrl
        : blazorOrigin;

    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = redirectTo },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme });
})
.WithName("BffLogout");

app.MapGet("/bff/user", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Ok(new { isAuthenticated = false });
    }

    return Results.Ok(new
    {
        isAuthenticated = true,
        username = user.Identity.Name,
        name = user.FindFirst("name")?.Value ?? user.Identity.Name
    });
})
.WithName("BffUser");

app.MapGet("/api/roles", async (MidpointService midpointService, ILogger<Program> logger) =>
{
    try
    {
        var roles = await midpointService.GetRolesAsync();
        return Results.Ok(roles);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "GET /api/roles: erreur d'authentification Midpoint");
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "GET /api/roles: Midpoint indisponible");
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("GetRoles")
.WithOpenApi();

app.MapGet("/api/catalogue", async (ClaimsPrincipal user, MidpointService midpointService, ILogger<Program> logger) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    try
    {
        var userOid = await midpointService.FindUserOidByUsernameAsync(username);
        if (userOid is null)
        {
            throw new MidpointUserNotFoundException($"Aucun utilisateur Midpoint trouvé pour '{username}'");
        }

        var orgOids = await midpointService.GetUserOrgOidsAsync(userOid);
        var roles = await midpointService.GetRequestableRolesForOrgsAsync(orgOids);

        var assignedRoleOids = await midpointService.GetUserAssignedRoleOidsAsync(userOid);
        var roleOidsEnCours = (await midpointService.GetUserCasesAsync(userOid))
            .Where(d => d.Statut == "Soumise")
            .Select(d => d.RoleOid)
            .ToHashSet();

        var rolesDisponibles = roles
            .Where(r => !assignedRoleOids.Contains(r.Oid) && !roleOidsEnCours.Contains(r.Oid))
            .ToList();

        return Results.Ok(rolesDisponibles);
    }
    catch (MidpointUserNotFoundException ex)
    {
        logger.LogWarning(ex, "GET /api/catalogue: utilisateur Midpoint introuvable pour '{Username}'", username);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "GET /api/catalogue: erreur d'authentification Midpoint");
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "GET /api/catalogue: Midpoint indisponible");
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("GetCatalogue")
.WithOpenApi();

// Les acces effectivement detenus par l'utilisateur : ce sont ses assignments de role
// dans Midpoint, c'est-a-dire ce qui lui a ete accorde (par une demande approuvee ou
// par une attribution directe), independamment de l'historique de ses demandes.
app.MapGet("/api/mes-acces", async (ClaimsPrincipal user, MidpointService midpointService, ILogger<Program> logger) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    try
    {
        var userOid = await midpointService.FindUserOidByUsernameAsync(username);
        if (userOid is null)
        {
            throw new MidpointUserNotFoundException($"Aucun utilisateur Midpoint trouvé pour '{username}'");
        }

        var assignedRoleOids = await midpointService.GetUserAssignedRoleOidsAsync(userOid);
        if (assignedRoleOids.Count == 0)
        {
            return Results.Ok(new List<RoleDto>());
        }

        var tousLesRoles = await midpointService.GetRolesAsync();
        var mesAcces = tousLesRoles
            .Where(r => assignedRoleOids.Contains(r.Oid))
            .ToList();

        return Results.Ok(mesAcces);
    }
    catch (MidpointUserNotFoundException ex)
    {
        logger.LogWarning(ex, "GET /api/mes-acces: utilisateur Midpoint introuvable pour '{Username}'", username);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "GET /api/mes-acces: erreur d'authentification Midpoint");
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "GET /api/mes-acces: Midpoint indisponible");
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("GetMesAcces")
.WithOpenApi();

app.MapGet("/api/demandes", async (ClaimsPrincipal user, MidpointService midpointService, ILogger<Program> logger) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    try
    {
        var userOid = await midpointService.FindUserOidByUsernameAsync(username);
        if (userOid is null)
        {
            throw new MidpointUserNotFoundException($"Aucun utilisateur Midpoint trouvé pour '{username}'");
        }

        var demandes = await midpointService.GetUserCasesAsync(userOid);
        return Results.Ok(demandes);
    }
    catch (MidpointUserNotFoundException ex)
    {
        logger.LogWarning(ex, "GET /api/demandes: utilisateur Midpoint introuvable pour '{Username}'", username);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "GET /api/demandes: erreur d'authentification Midpoint");
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "GET /api/demandes: Midpoint indisponible");
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("GetDemandes")
.WithOpenApi();

app.MapPost("/api/demandes", async (ClaimsPrincipal user, NouvelleDemandeRequest body, MidpointService midpointService, ILogger<Program> logger) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(body.RoleOid))
    {
        return Results.BadRequest(new { error = "RoleOid requis" });
    }

    try
    {
        var userOid = await midpointService.FindUserOidByUsernameAsync(username);
        if (userOid is null)
        {
            throw new MidpointUserNotFoundException($"Aucun utilisateur Midpoint trouvé pour '{username}'");
        }

        var orgOids = await midpointService.GetUserOrgOidsAsync(userOid);
        var catalogueRoles = await midpointService.GetRequestableRolesForOrgsAsync(orgOids);
        if (!catalogueRoles.Any(r => r.Oid == body.RoleOid))
        {
            logger.LogWarning(
                "POST /api/demandes: tentative de demande pour un role hors catalogue (user={Username}, roleOid={RoleOid})",
                username, body.RoleOid);
            return Results.Json(new { error = "Ce rôle ne fait pas partie de votre catalogue d'accès." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var assignedRoleOids = await midpointService.GetUserAssignedRoleOidsAsync(userOid);
        if (assignedRoleOids.Contains(body.RoleOid))
        {
            return Results.Json(new { error = "Vous disposez déjà de ce rôle." }, statusCode: StatusCodes.Status409Conflict);
        }

        var demandesEnCours = await midpointService.GetUserCasesAsync(userOid);
        if (demandesEnCours.Any(d => d.RoleOid == body.RoleOid && d.Statut == "Soumise"))
        {
            return Results.Json(new { error = "Une demande est déjà en cours pour ce rôle." }, statusCode: StatusCodes.Status409Conflict);
        }

        await midpointService.CreateAccessRequestAsync(userOid, body.RoleOid, body.Justification);
        return Results.Accepted();
    }
    catch (MidpointUserNotFoundException ex)
    {
        logger.LogWarning(ex, "POST /api/demandes: utilisateur Midpoint introuvable pour '{Username}'", username);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "POST /api/demandes: erreur d'authentification Midpoint");
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "POST /api/demandes: Midpoint indisponible");
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("PostDemande")
.WithOpenApi();

app.MapDelete("/api/demandes/{roleOid}", async (string roleOid, ClaimsPrincipal user, MidpointService midpointService, ILogger<Program> logger) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    try
    {
        var userOid = await midpointService.FindUserOidByUsernameAsync(username);
        if (userOid is null)
        {
            throw new MidpointUserNotFoundException($"Aucun utilisateur Midpoint trouvé pour '{username}'");
        }

        var demandesEnCours = await midpointService.GetUserCasesAsync(userOid);
        var demande = demandesEnCours.FirstOrDefault(d => d.RoleOid == roleOid && d.Statut == "Soumise");
        if (demande is null)
        {
            return Results.Json(new { error = "Aucune demande en cours pour ce rôle." }, statusCode: StatusCodes.Status404NotFound);
        }

        await midpointService.CancelAccessRequestAsync(userOid, roleOid);
        return Results.NoContent();
    }
    catch (MidpointUserNotFoundException ex)
    {
        logger.LogWarning(ex, "DELETE /api/demandes/{RoleOid}: utilisateur Midpoint introuvable pour '{Username}'", roleOid, username);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status404NotFound);
    }
    catch (MidpointAuthenticationException ex)
    {
        logger.LogError(ex, "DELETE /api/demandes/{RoleOid}: erreur d'authentification Midpoint", roleOid);
        return Results.Json(new { error = "Erreur d'authentification vers Midpoint" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (MidpointUnavailableException ex)
    {
        logger.LogError(ex, "DELETE /api/demandes/{RoleOid}: Midpoint indisponible", roleOid);
        return Results.Json(new { error = "Midpoint indisponible" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireAuthorization()
.WithName("DeleteDemande")
.WithOpenApi();

app.MapGet("/api/health", async (MidpointService midpointService, ILogger<Program> logger) =>
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await midpointService.PingAsync(cts.Token);
        return Results.Ok(new { status = "ok", midpoint = "reachable" });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "GET /api/health: Midpoint injoignable");
        return Results.Json(new { status = "degraded", midpoint = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetHealth")
.WithOpenApi();

app.Run();
