namespace InterfaceExterneIAM.Models;

public class DemandeDto
{
    public string CaseOid { get; set; } = string.Empty;
    public string RoleOid { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    public DateTimeOffset? DateCreation { get; set; }
}
