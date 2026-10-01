using Microsoft.AspNetCore.Mvc;
using ProvisioningGateway.Api.Models;
using ProvisioningGateway.Api.Services;

namespace ProvisioningGateway.Api.Controllers;

[ApiController]
[Route("api/comptes")]
public class ComptesController : ControllerBase
{
    private readonly IProvisioningService _provisioningService;
    private readonly ILogger<ComptesController> _logger;

    public ComptesController(IProvisioningService provisioningService, ILogger<ComptesController> logger)
    {
        _provisioningService = provisioningService;
        _logger = logger;
    }

    // Appele par midPoint (connecteur REST generique) lors du provisioning
    // d'un compte sur cette Resource, typiquement juste apres l'approbation
    // d'une demande d'acces dans le portail.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId) || string.IsNullOrWhiteSpace(request.Login))
        {
            return BadRequest(new { error = "resourceId et login sont requis." });
        }

        try
        {
            var result = await _provisioningService.CreateAsync(request);
            return Created($"/api/comptes/{result.Uid}", new { uid = result.Uid });
        }
        catch (MappingNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnknownRoleException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la creation du compte");
            return Problem(detail: "Erreur interne lors du provisioning.", statusCode: 500);
        }
    }

    // Appele par midPoint pour relire l'etat d'un compte (verification post-creation,
    // reconciliation periodique).
    [HttpGet("{uid}")]
    public async Task<IActionResult> Read(string uid, [FromQuery] string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return BadRequest(new { error = "resourceId requis en query string." });
        }

        try
        {
            var compte = await _provisioningService.ReadAsync(resourceId, uid);
            return compte is null
                ? NotFound(new { error = $"Aucun compte trouve pour uid='{uid}'." })
                : Ok(compte);
        }
        catch (MappingNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la lecture du compte {Uid}", uid);
            return Problem(detail: "Erreur interne lors de la lecture.", statusCode: 500);
        }
    }

    // Appele par midPoint quand les roles affectes a l'utilisateur changent
    // (ajout/retrait d'un acces deja provisionne sur cette ressource).
    [HttpPatch("{uid}")]
    public async Task<IActionResult> Update(string uid, [FromBody] UpdateCompteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId))
        {
            return BadRequest(new { error = "resourceId requis." });
        }

        try
        {
            await _provisioningService.UpdateAsync(request.ResourceId, uid, request);
            return NoContent();
        }
        catch (MappingNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (CompteNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnknownRoleException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la mise a jour du compte {Uid}", uid);
            return Problem(detail: "Erreur interne lors de la mise a jour.", statusCode: 500);
        }
    }

    // Appele par midPoint quand l'acces doit etre retire. Desactive le compte
    // (voir SqliteAccountRepository.DeleteAsync) plutot que de le supprimer physiquement.
    [HttpDelete("{uid}")]
    public async Task<IActionResult> Delete(string uid, [FromQuery] string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return BadRequest(new { error = "resourceId requis en query string." });
        }

        try
        {
            await _provisioningService.DeleteAsync(resourceId, uid);
            return NoContent();
        }
        catch (MappingNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (CompteNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la suppression du compte {Uid}", uid);
            return Problem(detail: "Erreur interne lors de la suppression.", statusCode: 500);
        }
    }
}
