using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace InterfaceExterneIAM.Services;

// Le BFF et Blazor sont sur des origines differentes : sans ce handler, le navigateur
// n'envoie/n'accepte pas le cookie de session cross-site pose apres le login Keycloak.
public class CredentialsIncludeHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
