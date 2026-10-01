using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InterfaceExterneIAM.Server.Dtos;
using InterfaceExterneIAM.Server.Exceptions;

namespace InterfaceExterneIAM.Server.Services;

public class MidpointService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MidpointService> _logger;

    public MidpointService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MidpointService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Midpoint");
        _logger = logger;

        var username = configuration["Midpoint:Username"]
            ?? throw new InvalidOperationException("Midpoint:Username manquant dans la configuration (user-secrets / variables d'environnement).");
        var password = configuration["Midpoint:Password"]
            ?? throw new InvalidOperationException("Midpoint:Password manquant dans la configuration (user-secrets / variables d'environnement).");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<MidpointRolesResponse>("roles");

            var roles = response?.Object?.Object ?? new List<MidpointRoleObject>();

            return roles
                .Select(r => new RoleDto { Name = r.Name ?? string.Empty, Oid = r.Oid ?? string.Empty })
                .ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de GET roles");
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de GET roles");
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de l'appel GET roles vers Midpoint");
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    public async Task<string?> FindUserOidByUsernameAsync(string username)
    {
        try
        {
            var query = new
            {
                query = new
                {
                    filter = new
                    {
                        equal = new { path = "name", value = username }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync("users/search", query);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MidpointSearchResponse<MidpointUserObject>>();
            var user = result?.Object?.Object?.FirstOrDefault();
            return user?.Oid;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de la recherche de l'utilisateur '{Username}'", username);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de la recherche de l'utilisateur '{Username}'", username);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de la recherche de l'utilisateur '{Username}'", username);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    public async Task<List<string>> GetUserOrgOidsAsync(string userOid)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<MidpointUserResponse>($"users/{userOid}");

            return result?.User?.ParentOrgRef?
                .Select(r => r.Oid)
                .Where(oid => !string.IsNullOrEmpty(oid))
                .Select(oid => oid!)
                .ToList()
                ?? new List<string>();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de GET users/{UserOid}", userOid);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de GET users/{UserOid}", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de GET users/{UserOid}", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    // Hypothese de modelisation : les roles requestable d'une org sont ceux qu'elle
    // "induit" (org.inducement -> RoleType). A ajuster si le modele Midpoint reel
    // utilise plutot roleMembershipRef ou une autre relation.
    public async Task<List<RoleDto>> GetRequestableRolesForOrgsAsync(IEnumerable<string> orgOids)
    {
        var roles = new Dictionary<string, RoleDto>();

        try
        {
            foreach (var orgOid in orgOids)
            {
                var orgResult = await _httpClient.GetFromJsonAsync<MidpointOrgResponse>($"orgs/{orgOid}");
                var roleOids = orgResult?.Org?.Inducement?
                    .Select(i => i.TargetRef?.Oid)
                    .Where(oid => !string.IsNullOrEmpty(oid))
                    .Select(oid => oid!)
                    .Distinct()
                    ?? Enumerable.Empty<string>();

                foreach (var roleOid in roleOids)
                {
                    if (roles.ContainsKey(roleOid))
                    {
                        continue;
                    }

                    var roleResult = await _httpClient.GetFromJsonAsync<MidpointRoleResponse>($"roles/{roleOid}");
                    var role = roleResult?.Role;
                    if (role is not null && role.Requestable == true)
                    {
                        roles[roleOid] = new RoleDto
                        {
                            Name = role.Name ?? string.Empty,
                            Oid = role.Oid ?? roleOid,
                            Description = role.Description
                        };
                    }
                }
            }

            return roles.Values.ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de la resolution des roles requestable");
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de la resolution des roles requestable");
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de la resolution des roles requestable");
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    public async Task<List<string>> GetUserAssignedRoleOidsAsync(string userOid)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<MidpointUserResponse>($"users/{userOid}");

            return result?.User?.Assignment?
                .Where(a => a.TargetRef?.Type?.Contains("RoleType", StringComparison.OrdinalIgnoreCase) == true)
                .Select(a => a.TargetRef!.Oid)
                .Where(oid => !string.IsNullOrEmpty(oid))
                .Select(oid => oid!)
                .ToList()
                ?? new List<string>();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de GET users/{UserOid} (assignments)", userOid);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de GET users/{UserOid} (assignments)", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de GET users/{UserOid} (assignments)", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    // Une demande d'acces = ajout d'un assignment (targetRef -> role) sur le user lui-meme.
    // Si un workflow d'approbation est configure sur le role, Midpoint intercepte cette
    // modification et cree un CaseType au lieu d'appliquer l'assignment immediatement.
    public async Task CreateAccessRequestAsync(string userOid, string roleOid, string justification)
    {
        try
        {
            var modification = new
            {
                objectModification = new
                {
                    itemDelta = new[]
                    {
                        new
                        {
                            modificationType = "add",
                            path = "assignment",
                            value = new[]
                            {
                                new
                                {
                                    targetRef = new { oid = roleOid, type = "RoleType" },
                                    description = justification
                                }
                            }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Patch, $"users/{userOid}")
            {
                Content = JsonContent.Create(modification)
            };
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Midpoint a renvoye {StatusCode} lors de la creation de la demande d'acces (user={UserOid}, role={RoleOid}) : {Body}",
                    (int)response.StatusCode, userOid, roleOid, body);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de la creation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de la creation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de la creation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    // Annule une demande en cours en retirant l'assignment cible du delta du user. Comme
    // l'assignment n'est pas encore materialise sur le user tant que le case est ouvert (il vit
    // dans l'approvalContext du case, pas dans user.assignment), ce delete n'a d'effet que si
    // Midpoint retrouve/annule le case correspondant a partir du meme changement demande.
    // A valider en conditions reelles : si ca ne suffit pas a fermer le case, il faudra passer
    // par une action dediee sur le case lui-meme plutot que sur l'objet user.
    public async Task CancelAccessRequestAsync(string userOid, string roleOid)
    {
        try
        {
            var modification = new
            {
                objectModification = new
                {
                    itemDelta = new[]
                    {
                        new
                        {
                            modificationType = "delete",
                            path = "assignment",
                            value = new[]
                            {
                                new
                                {
                                    targetRef = new { oid = roleOid, type = "RoleType" }
                                }
                            }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Patch, $"users/{userOid}")
            {
                Content = JsonContent.Create(modification)
            };
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Midpoint a renvoye {StatusCode} lors de l'annulation de la demande d'acces (user={UserOid}, role={RoleOid}) : {Body}",
                    (int)response.StatusCode, userOid, roleOid, body);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de l'annulation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de l'annulation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de l'annulation de la demande d'acces (user={UserOid}, role={RoleOid})", userOid, roleOid);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    // Liste les cases Midpoint dont l'objet cible (objectRef) est l'utilisateur : ce sont les
    // demandes d'acces (en cours ou terminees) qui le concernent, quel que soit le compte
    // technique ayant execute la modification a l'origine (le BFF agit toujours via le compte
    // de service Midpoint, jamais avec les identifiants de l'utilisateur final).
    public async Task<List<DemandeDto>> GetUserCasesAsync(string userOid)
    {
        try
        {
            var query = new
            {
                query = new
                {
                    filter = new
                    {
                        @ref = new
                        {
                            path = "objectRef",
                            value = new[] { new { oid = userOid } }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync("cases/search", query);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MidpointCaseSearchResponse>();
            var cases = result?.Object?.Object ?? new List<MidpointCaseObject>();

            // Midpoint cree un case racine ("Approving and executing change of user ...") qui
            // orchestre l'operation, un case enfant par element approuve ("Assigning role X to
            // user Y", targetRef = le role), et parfois un case technique supplementaire
            // ("Changes that do not require approval") pour la partie du delta ne necessitant pas
            // d'approbation. Seuls les cases enfants portant sur un role (targetRef de type
            // RoleType) representent une vraie demande d'acces pour l'utilisateur.
            return cases
                .Where(c => c.ParentRef is not null
                    && c.TargetRef?.Type?.Contains("RoleType", StringComparison.OrdinalIgnoreCase) == true)
                .Select(c => new DemandeDto
                {
                    CaseOid = c.Oid ?? string.Empty,
                    RoleOid = c.TargetRef?.Oid ?? string.Empty,
                    RoleName = ExtractRoleName(c.Name),
                    Statut = MapCaseStatut(c.State, c.Outcome),
                    DateCreation = c.Metadata?.CreateTimestamp
                }).ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Midpoint a refuse l'authentification lors de la recuperation des demandes (user={UserOid})", userOid);
            throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors de la recuperation des demandes (user={UserOid})", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors de la recuperation des demandes (user={UserOid})", userOid);
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    private static string MapCaseStatut(string? state, string? outcome)
    {
        if (!string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return "Soumise";
        }

        if (outcome?.Contains("reject", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Rejetée";
        }

        if (outcome?.Contains("approve", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Validée";
        }

        return "Soumise";
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("users", cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MidpointAuthenticationException("Erreur d'authentification vers Midpoint");
            }

            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Midpoint injoignable lors du health check");
            throw new MidpointUnavailableException("Midpoint indisponible", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout lors du health check Midpoint");
            throw new MidpointUnavailableException("Midpoint indisponible (timeout)", ex);
        }
    }

    private class MidpointRolesResponse
    {
        [JsonPropertyName("object")]
        public MidpointRolesWrapper? Object { get; set; }
    }

    private class MidpointRolesWrapper
    {
        [JsonPropertyName("object")]
        public List<MidpointRoleObject>? Object { get; set; }
    }

    private class MidpointRoleObject
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("oid")]
        public string? Oid { get; set; }
    }

    private class MidpointSearchResponse<T>
    {
        [JsonPropertyName("object")]
        public MidpointSearchWrapper<T>? Object { get; set; }
    }

    private class MidpointSearchWrapper<T>
    {
        [JsonPropertyName("object")]
        public List<T>? Object { get; set; }
    }

    private class MidpointUserObject
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class MidpointUserResponse
    {
        [JsonPropertyName("user")]
        public MidpointUserDetail? User { get; set; }
    }

    private class MidpointUserDetail
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("parentOrgRef")]
        [JsonConverter(typeof(SingleOrArrayConverter<MidpointRef>))]
        public List<MidpointRef>? ParentOrgRef { get; set; }

        [JsonPropertyName("assignment")]
        [JsonConverter(typeof(SingleOrArrayConverter<MidpointAssignment>))]
        public List<MidpointAssignment>? Assignment { get; set; }
    }

    private class MidpointRef
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private class MidpointOrgResponse
    {
        [JsonPropertyName("org")]
        public MidpointOrgDetail? Org { get; set; }
    }

    private class MidpointOrgDetail
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("inducement")]
        [JsonConverter(typeof(SingleOrArrayConverter<MidpointAssignment>))]
        public List<MidpointAssignment>? Inducement { get; set; }
    }

    private class MidpointAssignment
    {
        [JsonPropertyName("targetRef")]
        public MidpointRef? TargetRef { get; set; }
    }

    private class MidpointRoleResponse
    {
        [JsonPropertyName("role")]
        public MidpointRoleDetail? Role { get; set; }
    }

    private class MidpointRoleDetail
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("requestable")]
        public bool? Requestable { get; set; }
    }

    private class MidpointCaseSearchResponse
    {
        [JsonPropertyName("object")]
        public MidpointCaseSearchWrapper? Object { get; set; }
    }

    private class MidpointCaseSearchWrapper
    {
        [JsonPropertyName("object")]
        [JsonConverter(typeof(SingleOrArrayConverter<MidpointCaseObject>))]
        public List<MidpointCaseObject>? Object { get; set; }
    }

    private class MidpointCaseObject
    {
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }

        [JsonPropertyName("name")]
        [JsonConverter(typeof(PolyStringConverter))]
        public string? Name { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("outcome")]
        public string? Outcome { get; set; }

        [JsonPropertyName("metadata")]
        public MidpointCaseMetadata? Metadata { get; set; }

        [JsonPropertyName("parentRef")]
        public MidpointRef? ParentRef { get; set; }

        [JsonPropertyName("targetRef")]
        public MidpointRef? TargetRef { get; set; }
    }

    private static readonly System.Text.RegularExpressions.Regex AssigningRoleNameRegex =
        new("^Assigning role \"(.+)\" to user", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Le nom du case enfant est une phrase generee par Midpoint (ex: Assigning role "X" to user "Y").
    // On en extrait le nom du role pour l'affichage ; a defaut de correspondance, on garde la phrase brute.
    private static string ExtractRoleName(string? caseName)
    {
        if (string.IsNullOrEmpty(caseName))
        {
            return string.Empty;
        }

        var match = AssigningRoleNameRegex.Match(caseName);
        return match.Success ? match.Groups[1].Value : caseName;
    }

    private class MidpointCaseMetadata
    {
        [JsonPropertyName("createTimestamp")]
        public DateTimeOffset? CreateTimestamp { get; set; }
    }

    // Midpoint serialise certains champs "name" en PolyString ({"orig": "...", "norm": "..."})
    // plutot qu'en simple chaine (observe sur CaseType). Ce converter accepte les deux formes.
    private class PolyStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                if (doc.RootElement.TryGetProperty("orig", out var orig))
                {
                    return orig.GetString();
                }
                return null;
            }

            throw new JsonException($"Impossible de convertir le token {reader.TokenType} en chaine (PolyString attendu).");
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    // Midpoint serialise les proprietes multivaluees comme un objet unique (pas un tableau)
    // quand il n'y a qu'une seule valeur (ex: parentOrgRef, inducement). Ce converter accepte les deux formes.
    private class SingleOrArrayConverter<T> : JsonConverter<List<T>?>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<T>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var item = JsonSerializer.Deserialize<T>(ref reader, options);
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }
                return list;
            }

            var single = JsonSerializer.Deserialize<T>(ref reader, options);
            return single is null ? new List<T>() : new List<T> { single };
        }

        public override void Write(Utf8JsonWriter writer, List<T>? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
