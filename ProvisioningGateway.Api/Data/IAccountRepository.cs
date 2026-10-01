using ProvisioningGateway.Api.Mapping;
using ProvisioningGateway.Api.Models;

namespace ProvisioningGateway.Api.Data;

public interface IAccountRepository
{
    Task<string> CreateAsync(ResourceMappingConfig mapping, CreateCompteRequest request);
    Task<CompteDto?> ReadAsync(ResourceMappingConfig mapping, string uid);
    Task UpdateAsync(ResourceMappingConfig mapping, string uid, UpdateCompteRequest request);
    Task DeleteAsync(ResourceMappingConfig mapping, string uid);
    Task<List<CompteDto>> ListAllAsync(ResourceMappingConfig mapping);
}
