using ProvisioningGateway.Api.Data;
using ProvisioningGateway.Api.Mapping;
using ProvisioningGateway.Api.Models;

namespace ProvisioningGateway.Api.Services;

// Chef d'orchestre : ne connait pas le SQL, delegue au repository.
// Charge le mapping, appelle le repository, journalise le resultat
// (succes ou echec) puisque l'application cible n'a elle-meme aucune
// trace de ces ecritures faites depuis l'exterieur.
public class ProvisioningService : IProvisioningService
{
    private readonly IMappingStore _mappingStore;
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<ProvisioningService> _logger;

    public ProvisioningService(
        IMappingStore mappingStore,
        IAccountRepository accountRepository,
        ILogger<ProvisioningService> logger)
    {
        _mappingStore = mappingStore;
        _accountRepository = accountRepository;
        _logger = logger;
    }

    public async Task<CreateCompteResult> CreateAsync(CreateCompteRequest request)
    {
        var mapping = _mappingStore.GetMapping(request.ResourceId)
            ?? throw new MappingNotFoundException(request.ResourceId);

        try
        {
            var uid = await _accountRepository.CreateAsync(mapping, request);

            _logger.LogInformation(
                "Provisioning OK : compte cree (resource={ResourceId}, login={Login}, uid={Uid}, roles={Roles})",
                request.ResourceId, request.Login, uid, string.Join(",", request.Roles));

            return new CreateCompteResult { Uid = uid };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Provisioning ECHEC : creation du compte impossible (resource={ResourceId}, login={Login})",
                request.ResourceId, request.Login);
            throw;
        }
    }

    public async Task<CompteDto?> ReadAsync(string resourceId, string uid)
    {
        var mapping = _mappingStore.GetMapping(resourceId)
            ?? throw new MappingNotFoundException(resourceId);

        return await _accountRepository.ReadAsync(mapping, uid);
    }

    public async Task UpdateAsync(string resourceId, string uid, UpdateCompteRequest request)
    {
        var mapping = _mappingStore.GetMapping(resourceId)
            ?? throw new MappingNotFoundException(resourceId);

        try
        {
            await _accountRepository.UpdateAsync(mapping, uid, request);
            _logger.LogInformation(
                "Provisioning OK : compte mis a jour (resource={ResourceId}, uid={Uid}, roles={Roles})",
                resourceId, uid, string.Join(",", request.Roles));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Provisioning ECHEC : mise a jour impossible (resource={ResourceId}, uid={Uid})", resourceId, uid);
            throw;
        }
    }

    public async Task DeleteAsync(string resourceId, string uid)
    {
        var mapping = _mappingStore.GetMapping(resourceId)
            ?? throw new MappingNotFoundException(resourceId);

        try
        {
            await _accountRepository.DeleteAsync(mapping, uid);
            _logger.LogInformation("Provisioning OK : compte desactive (resource={ResourceId}, uid={Uid})", resourceId, uid);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Provisioning ECHEC : desactivation impossible (resource={ResourceId}, uid={Uid})", resourceId, uid);
            throw;
        }
    }
}
