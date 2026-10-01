namespace ProvisioningGateway.Api.Mapping;

// Decrit comment un "compte" cote midPoint (login, email, roles) se traduit
// en tables/colonnes SQL reelles pour une application cible donnee.
// Une entree = une application legacy branchee sur le Gateway.
public class ResourceMappingConfig
{
    public string ResourceId { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public TableMapping Comptes { get; set; } = new();
    public TableMapping Roles { get; set; } = new();
    public JunctionTableMapping Affectations { get; set; } = new();
}

public class TableMapping
{
    public string Table { get; set; } = string.Empty;
    public string CleId { get; set; } = "id";
    public string ColonneLogin { get; set; } = "login";
    public string ColonneEmail { get; set; } = "email";
    public string ColonneActif { get; set; } = "actif";
    public string ColonneCode { get; set; } = "code";
}

public class JunctionTableMapping
{
    public string Table { get; set; } = string.Empty;
    public string FkCompte { get; set; } = string.Empty;
    public string FkRole { get; set; } = string.Empty;
}
