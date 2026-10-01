using Dapper;
using Microsoft.Data.Sqlite;
using ProvisioningGateway.Api.Mapping;
using ProvisioningGateway.Api.Models;

namespace ProvisioningGateway.Api.Data;

// Implementation de demo pour le PoC : cible SQLite (fichier local),
// pour pouvoir tester le flux de bout en bout sans dependre d'un vrai
// serveur SQL. En conditions reelles, on remplacerait SqliteConnection
// par le provider ADO.NET correspondant au SGBD de l'appli cible
// (Npgsql pour PostgreSQL, SqlConnection pour SQL Server...), le reste
// de la logique (mapping, transaction, SQL parametre) ne change pas.
//
// Note de securite : les noms de tables/colonnes viennent du mapping
// (config admin, fiable) et sont inseres tels quels dans le texte SQL —
// c'est la seule maniere de parametrer des identifiants en ADO.NET.
// Les VALEURS (login, email, roles) viennent, elles, du corps de requete
// et passent TOUJOURS par des parametres SQL (@Login, @Email...), jamais
// par concatenation, pour eviter toute injection SQL.
public class SqliteAccountRepository : IAccountRepository
{
    public async Task<string> CreateAsync(ResourceMappingConfig mapping, CreateCompteRequest request)
    {
        using var connection = new SqliteConnection(mapping.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var insertCompteSql = $@"
                INSERT INTO {mapping.Comptes.Table}
                    ({mapping.Comptes.ColonneLogin}, {mapping.Comptes.ColonneEmail}, {mapping.Comptes.ColonneActif})
                VALUES (@Login, @Email, 1);
                SELECT last_insert_rowid();";

            var compteId = await connection.ExecuteScalarAsync<long>(
                insertCompteSql,
                new { request.Login, request.Email },
                transaction);

            foreach (var roleCode in request.Roles)
            {
                var roleIdSql = $@"
                    SELECT {mapping.Roles.CleId}
                    FROM {mapping.Roles.Table}
                    WHERE {mapping.Roles.ColonneCode} = @Code";

                var roleId = await connection.ExecuteScalarAsync<long?>(
                    roleIdSql, new { Code = roleCode }, transaction);

                if (roleId is null)
                {
                    throw new UnknownRoleException(roleCode);
                }

                var insertAffectationSql = $@"
                    INSERT INTO {mapping.Affectations.Table}
                        ({mapping.Affectations.FkCompte}, {mapping.Affectations.FkRole})
                    VALUES (@CompteId, @RoleId);";

                await connection.ExecuteAsync(
                    insertAffectationSql,
                    new { CompteId = compteId, RoleId = roleId },
                    transaction);
            }

            transaction.Commit();
            return compteId.ToString();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<CompteDto?> ReadAsync(ResourceMappingConfig mapping, string uid)
    {
        using var connection = new SqliteConnection(mapping.ConnectionString);
        await connection.OpenAsync();

        var compteSql = $@"
            SELECT {mapping.Comptes.CleId} AS Uid,
                   {mapping.Comptes.ColonneLogin} AS Login,
                   {mapping.Comptes.ColonneEmail} AS Email,
                   {mapping.Comptes.ColonneActif} AS Actif
            FROM {mapping.Comptes.Table}
            WHERE {mapping.Comptes.CleId} = @Uid";

        var compte = await connection.QuerySingleOrDefaultAsync<CompteRow>(compteSql, new { Uid = uid });
        if (compte is null)
        {
            return null;
        }

        var rolesSql = $@"
            SELECT r.{mapping.Roles.ColonneCode}
            FROM {mapping.Affectations.Table} ur
            JOIN {mapping.Roles.Table} r ON r.{mapping.Roles.CleId} = ur.{mapping.Affectations.FkRole}
            WHERE ur.{mapping.Affectations.FkCompte} = @Uid";

        var roles = (await connection.QueryAsync<string>(rolesSql, new { Uid = uid })).ToList();

        return new CompteDto
        {
            Uid = compte.Uid.ToString(),
            Login = compte.Login,
            Email = compte.Email,
            Actif = compte.Actif == 1,
            Roles = roles
        };
    }

    public async Task UpdateAsync(ResourceMappingConfig mapping, string uid, UpdateCompteRequest request)
    {
        using var connection = new SqliteConnection(mapping.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var existsSql = $"SELECT COUNT(*) FROM {mapping.Comptes.Table} WHERE {mapping.Comptes.CleId} = @Uid";
            var exists = await connection.ExecuteScalarAsync<long>(existsSql, new { Uid = uid }, transaction);
            if (exists == 0)
            {
                throw new CompteNotFoundException(uid);
            }

            if (request.Email is not null)
            {
                var updateSql = $@"
                    UPDATE {mapping.Comptes.Table}
                    SET {mapping.Comptes.ColonneEmail} = @Email
                    WHERE {mapping.Comptes.CleId} = @Uid";
                await connection.ExecuteAsync(updateSql, new { Email = request.Email, Uid = uid }, transaction);
            }

            var currentRolesSql = $@"
                SELECT r.{mapping.Roles.ColonneCode}
                FROM {mapping.Affectations.Table} ur
                JOIN {mapping.Roles.Table} r ON r.{mapping.Roles.CleId} = ur.{mapping.Affectations.FkRole}
                WHERE ur.{mapping.Affectations.FkCompte} = @Uid";
            var currentRoles = (await connection.QueryAsync<string>(currentRolesSql, new { Uid = uid }, transaction)).ToHashSet();

            var desiredRoles = request.Roles.ToHashSet();
            var rolesToAdd = desiredRoles.Except(currentRoles);
            var rolesToRemove = currentRoles.Except(desiredRoles);

            foreach (var roleCode in rolesToAdd)
            {
                var roleIdSql = $"SELECT {mapping.Roles.CleId} FROM {mapping.Roles.Table} WHERE {mapping.Roles.ColonneCode} = @Code";
                var roleId = await connection.ExecuteScalarAsync<long?>(roleIdSql, new { Code = roleCode }, transaction)
                    ?? throw new UnknownRoleException(roleCode);

                var insertSql = $@"
                    INSERT INTO {mapping.Affectations.Table} ({mapping.Affectations.FkCompte}, {mapping.Affectations.FkRole})
                    VALUES (@Uid, @RoleId)";
                await connection.ExecuteAsync(insertSql, new { Uid = uid, RoleId = roleId }, transaction);
            }

            foreach (var roleCode in rolesToRemove)
            {
                var deleteSql = $@"
                    DELETE FROM {mapping.Affectations.Table}
                    WHERE {mapping.Affectations.FkCompte} = @Uid
                      AND {mapping.Affectations.FkRole} = (
                          SELECT {mapping.Roles.CleId} FROM {mapping.Roles.Table} WHERE {mapping.Roles.ColonneCode} = @Code
                      )";
                await connection.ExecuteAsync(deleteSql, new { Uid = uid, Code = roleCode }, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(ResourceMappingConfig mapping, string uid)
    {
        using var connection = new SqliteConnection(mapping.ConnectionString);
        await connection.OpenAsync();

        var existsSql = $"SELECT COUNT(*) FROM {mapping.Comptes.Table} WHERE {mapping.Comptes.CleId} = @Uid";
        var exists = await connection.ExecuteScalarAsync<long>(existsSql, new { Uid = uid });
        if (exists == 0)
        {
            throw new CompteNotFoundException(uid);
        }

        // Desactivation logique plutot que suppression physique : convention frequente
        // sur les applis legacy (on ne perd jamais l'historique des comptes).
        var deactivateSql = $@"
            UPDATE {mapping.Comptes.Table}
            SET {mapping.Comptes.ColonneActif} = 0
            WHERE {mapping.Comptes.CleId} = @Uid";
        await connection.ExecuteAsync(deactivateSql, new { Uid = uid });
    }

    public async Task<List<CompteDto>> ListAllAsync(ResourceMappingConfig mapping)
    {
        using var connection = new SqliteConnection(mapping.ConnectionString);
        await connection.OpenAsync();

        var comptesSql = $@"
            SELECT {mapping.Comptes.CleId} AS Uid,
                   {mapping.Comptes.ColonneLogin} AS Login,
                   {mapping.Comptes.ColonneEmail} AS Email,
                   {mapping.Comptes.ColonneActif} AS Actif
            FROM {mapping.Comptes.Table}
            ORDER BY {mapping.Comptes.CleId}";

        var comptes = (await connection.QueryAsync<CompteRow>(comptesSql)).ToList();

        var result = new List<CompteDto>();
        foreach (var compte in comptes)
        {
            var rolesSql = $@"
                SELECT r.{mapping.Roles.ColonneCode}
                FROM {mapping.Affectations.Table} ur
                JOIN {mapping.Roles.Table} r ON r.{mapping.Roles.CleId} = ur.{mapping.Affectations.FkRole}
                WHERE ur.{mapping.Affectations.FkCompte} = @Uid";
            var roles = (await connection.QueryAsync<string>(rolesSql, new { Uid = compte.Uid })).ToList();

            result.Add(new CompteDto
            {
                Uid = compte.Uid.ToString(),
                Login = compte.Login,
                Email = compte.Email,
                Actif = compte.Actif == 1,
                Roles = roles
            });
        }

        return result;
    }

    private class CompteRow
    {
        public long Uid { get; set; }
        public string Login { get; set; } = string.Empty;
        public string? Email { get; set; }
        public long Actif { get; set; }
    }
}
