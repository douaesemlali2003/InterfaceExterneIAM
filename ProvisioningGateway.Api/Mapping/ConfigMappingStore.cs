namespace ProvisioningGateway.Api.Mapping;

// v1 : le mapping vient de appsettings.json ("ResourceMappings").
// Evolution prevue : remplacer par un store persiste (base propre au Gateway)
// pilote par une petite UI d'administration, sans toucher au reste du code
// puisque tout passe par IMappingStore.
public class ConfigMappingStore : IMappingStore
{
    private readonly Dictionary<string, ResourceMappingConfig> _mappings;

    public ConfigMappingStore(IConfiguration configuration)
    {
        var list = configuration.GetSection("ResourceMappings").Get<List<ResourceMappingConfig>>()
            ?? new List<ResourceMappingConfig>();

        _mappings = list.ToDictionary(m => m.ResourceId, StringComparer.OrdinalIgnoreCase);
    }

    public ResourceMappingConfig? GetMapping(string resourceId)
    {
        return _mappings.TryGetValue(resourceId, out var mapping) ? mapping : null;
    }
}
