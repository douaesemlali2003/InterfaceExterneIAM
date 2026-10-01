namespace ProvisioningGateway.Api.Models;

public class MappingNotFoundException : Exception
{
    public MappingNotFoundException(string resourceId)
        : base($"Aucun mapping configure pour resourceId='{resourceId}'.")
    {
    }
}

public class UnknownRoleException : Exception
{
    public UnknownRoleException(string roleCode)
        : base($"Role inconnu dans l'application cible : '{roleCode}'.")
    {
    }
}

public class CompteNotFoundException : Exception
{
    public CompteNotFoundException(string uid)
        : base($"Aucun compte trouve pour uid='{uid}'.")
    {
    }
}
