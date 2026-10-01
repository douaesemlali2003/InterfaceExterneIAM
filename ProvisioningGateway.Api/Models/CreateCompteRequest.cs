namespace ProvisioningGateway.Api.Models;

public class CreateCompteRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
}
