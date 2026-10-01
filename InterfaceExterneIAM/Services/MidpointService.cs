using System.Net.Http.Json;
using InterfaceExterneIAM.Models;

namespace InterfaceExterneIAM.Services;

public class MidpointService
{
    private readonly HttpClient _httpClient;

    public MidpointService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Midpoint");
    }

    public async Task<List<RoleDto>> GetCatalogueAsync()
    {
        var response = await _httpClient.GetAsync("api/catalogue");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Le BFF a renvoye une erreur ({(int)response.StatusCode})");
        }

        var roles = await response.Content.ReadFromJsonAsync<List<RoleDto>>();
        return roles ?? new List<RoleDto>();
    }

    public async Task<List<RoleDto>> GetMesAccesAsync()
    {
        var response = await _httpClient.GetAsync("api/mes-acces");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Le BFF a renvoye une erreur ({(int)response.StatusCode})");
        }

        var acces = await response.Content.ReadFromJsonAsync<List<RoleDto>>();
        return acces ?? new List<RoleDto>();
    }

    public async Task<List<DemandeDto>> GetMesDemandesAsync()
    {
        var response = await _httpClient.GetAsync("api/demandes");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Le BFF a renvoye une erreur ({(int)response.StatusCode})");
        }

        var demandes = await response.Content.ReadFromJsonAsync<List<DemandeDto>>();
        return demandes ?? new List<DemandeDto>();
    }

    public async Task SoumettreDemandeAsync(string roleOid, string justification)
    {
        var response = await _httpClient.PostAsJsonAsync("api/demandes", new { RoleOid = roleOid, Justification = justification });

        if (!response.IsSuccessStatusCode)
        {
            var erreur = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            throw new HttpRequestException(erreur?.Error ?? $"Le BFF a renvoye une erreur ({(int)response.StatusCode})");
        }
    }

    public async Task AnnulerDemandeAsync(string roleOid)
    {
        var response = await _httpClient.DeleteAsync($"api/demandes/{Uri.EscapeDataString(roleOid)}");

        if (!response.IsSuccessStatusCode)
        {
            var erreur = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            throw new HttpRequestException(erreur?.Error ?? $"Le BFF a renvoye une erreur ({(int)response.StatusCode})");
        }
    }

    private class ErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

