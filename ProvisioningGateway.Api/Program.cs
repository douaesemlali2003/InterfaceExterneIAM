using ProvisioningGateway.Api.Data;
using ProvisioningGateway.Api.Mapping;
using ProvisioningGateway.Api.Models;
using ProvisioningGateway.Api.Security;
using ProvisioningGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IMappingStore, ConfigMappingStore>();
builder.Services.AddScoped<IAccountRepository, SqliteAccountRepository>();
builder.Services.AddScoped<IProvisioningService, ProvisioningService>();

var app = builder.Build();

// Demo uniquement : cree la base SQLite locale simulant Application X au
// premier demarrage si elle n'existe pas encore.
if (app.Environment.IsDevelopment())
{
    var demoConnectionString = builder.Configuration["ResourceMappings:0:ConnectionString"];
    if (!string.IsNullOrEmpty(demoConnectionString))
    {
        ProvisioningGateway.Api.Data.DemoDatabaseSeeder.EnsureCreated(demoConnectionString);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Authentifie les appels midPoint -> Gateway via cle API (voir Security/ApiKeyMiddleware.cs).
app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

// Visualiseur de demo pour "AppX" (hors /api, donc pas de cle API requise) :
// permet de voir en direct l'etat de la base cible pendant une demonstration,
// sans que ce soit un vrai endpoint metier appele par midPoint.
app.MapGet("/appx/comptes", async (IMappingStore mappingStore, IAccountRepository repository) =>
{
    var mapping = mappingStore.GetMapping("appx");
    if (mapping is null)
    {
        return Results.NotFound(new { error = "Mapping 'appx' introuvable." });
    }

    var comptes = await repository.ListAllAsync(mapping);
    return Results.Ok(comptes);
});

app.Run();
