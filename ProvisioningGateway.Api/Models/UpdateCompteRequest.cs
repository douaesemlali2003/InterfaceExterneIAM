namespace ProvisioningGateway.Api.Models;

public class UpdateCompteRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public string? Email { get; set; }
    // Liste complete des roles souhaites : le repository calcule lui-meme
    // le diff (roles a ajouter / a retirer) par rapport a l'etat actuel.
    public List<string> Roles { get; set; } = new();
}
