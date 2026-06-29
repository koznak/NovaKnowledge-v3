using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NovaKnowledge.Infrastructure.Persistence;

public static class DatabaseBootstrap
{
    /// <summary>
    /// Ensures the PostgreSQL database exists, applies EF migrations, and seeds data.
    /// On first run, EF may log a transient error while checking migration history before the
    /// __EFMigrationsHistory table exists; migration still succeeds afterward.
    /// </summary>
    public static async Task EnsureDatabaseAsync(
        NovaKnowledgeDbContext db,
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await EnsurePostgresDatabaseExistsAsync(connectionString, logger, cancellationToken);

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied.");

        await DbSeeder.SeedAsync(db);
    }

    private static async Task EnsurePostgresDatabaseExistsAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
            return;

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
        checkCmd.Parameters.AddWithValue("name", databaseName);
        var exists = await checkCmd.ExecuteScalarAsync(cancellationToken) is not null;

        if (exists)
            return;

        logger.LogInformation("Creating PostgreSQL database {DatabaseName}...", databaseName);
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\"";
        await createCmd.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Database {DatabaseName} created.", databaseName);
    }
}
