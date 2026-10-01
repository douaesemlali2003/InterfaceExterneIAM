using Microsoft.Data.Sqlite;

namespace ProvisioningGateway.Api.Data;

// Cree et peuple une base SQLite locale simulant la base de "Application X" —
// uniquement pour permettre de demontrer le flux de bout en bout sans avoir
// besoin d'une vraie application legacy sous la main. Ne fait rien si le
// fichier existe deja (pour ne pas ecraser des donnees de test accumulees).
public static class DemoDatabaseSeeder
{
    public static void EnsureCreated(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (File.Exists(dataSource))
        {
            return;
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                login TEXT NOT NULL UNIQUE,
                email TEXT,
                actif INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE roles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL UNIQUE,
                libelle TEXT
            );

            CREATE TABLE user_roles (
                user_id INTEGER NOT NULL REFERENCES users(id),
                role_id INTEGER NOT NULL REFERENCES roles(id),
                PRIMARY KEY (user_id, role_id)
            );

            INSERT INTO roles (code, libelle) VALUES
                ('comptable', 'Comptable Application X'),
                ('administrateur', 'Administrateur Application X'),
                ('lecture_seule', 'Lecture seule Application X');
        ";
        command.ExecuteNonQuery();
    }
}
