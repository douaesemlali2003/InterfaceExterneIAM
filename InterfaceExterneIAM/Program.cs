using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using InterfaceExterneIAM;
using InterfaceExterneIAM.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddTransient<CredentialsIncludeHandler>();
builder.Services.AddHttpClient("Midpoint", client =>
{
    // Appelle le BFF (InterfaceExterneIAM.Server), pas Midpoint directement :
    // ca evite le CORS et n'expose plus les identifiants Midpoint au navigateur.
    // HTTPS obligatoire : le cookie de session du BFF est SameSite=None;Secure.
    client.BaseAddress = new Uri(BffConfig.BaseUrl);
})
.AddHttpMessageHandler<CredentialsIncludeHandler>();
builder.Services.AddScoped<MidpointService>();
builder.Services.AddScoped<AuthStateService>();

await builder.Build().RunAsync();
