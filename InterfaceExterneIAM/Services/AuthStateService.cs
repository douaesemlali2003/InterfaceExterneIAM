using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace InterfaceExterneIAM.Services;

public class AuthStateService
{
    private readonly HttpClient _httpClient;

    public bool IsAuthenticated { get; private set; }
    public string? Username { get; private set; }

    public AuthStateService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Midpoint");
    }

    public async Task RefreshAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<BffUserResponse>("bff/user");
        IsAuthenticated = result?.IsAuthenticated ?? false;
        Username = result?.Name ?? result?.Username;
    }

    private class BffUserResponse
    {
        [JsonPropertyName("isAuthenticated")]
        public bool IsAuthenticated { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
