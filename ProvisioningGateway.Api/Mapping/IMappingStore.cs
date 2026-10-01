namespace ProvisioningGateway.Api.Mapping;

public interface IMappingStore
{
    ResourceMappingConfig? GetMapping(string resourceId);
}
