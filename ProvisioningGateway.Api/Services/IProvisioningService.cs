using ProvisioningGateway.Api.Models;

namespace ProvisioningGateway.Api.Services;

public interface IProvisioningService
{
    Task<CreateCompteResult> CreateAsync(CreateCompteRequest request);
    Task<CompteDto?> ReadAsync(string resourceId, string uid);
    Task UpdateAsync(string resourceId, string uid, UpdateCompteRequest request);
    Task DeleteAsync(string resourceId, string uid);
}
