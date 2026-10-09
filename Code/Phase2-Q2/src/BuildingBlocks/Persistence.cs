using System.Reflection;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FoodDelivery.BuildingBlocks;

/// <summary>Helpers shared by every module's data access.</summary>
public static class Persistence
{
    /// <summary>
    /// Each module asks for ITS OWN connection string ("ConnectionStrings:Ordering"), falling back to
    /// the shared "Default". Q2 (D17): in production each module gets its OWN database ROLE (see db-roles.sql), allowed to
    /// write only its own schema - ownership enforced by PostgreSQL itself. Only configuration changes.
    /// </summary>
    public static string ConnectionStringFor(this IConfiguration configuration, string moduleName) =>
        configuration.GetConnectionString(moduleName)
        ?? configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("No connection string configured.");

    /// <summary>Reads a module's Data/schema.sql, which is compiled into the module as an embedded resource.</summary>
    public static string ReadEmbeddedSql(Assembly assembly, string fileName)
    {
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Runs every module's schema script on startup (all scripts use IF NOT EXISTS).</summary>
    public static async Task EnsureSchemasAsync(string connectionString, IEnumerable<IModule> modules)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Q2: platform tables first (job queue + feature flags), then each module's own schema.
        await using (var platform = new NpgsqlCommand(PlatformSchema.Sql, connection))
            await platform.ExecuteNonQueryAsync();

        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.SchemaSql)) continue;
            await using var command = new NpgsqlCommand(module.SchemaSql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
