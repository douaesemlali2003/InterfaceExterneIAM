namespace ProvisioningGateway.Api.Models;

public class CompteDto
{
    public string Uid { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Actif { get; set; }
    public List<string> Roles { get; set; } = new();
}
