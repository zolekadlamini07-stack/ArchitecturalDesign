using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FoodDelivery.BuildingBlocks;

// =============================================================================================
// FEATURE FLAGS (Q2, D16) - a light switch for a feature.
// New code is DEPLOYED switched off, then switched on when the business is ready. Deploying
// stops being the same thing as releasing, which takes risk out of deployments (problem P5).
// A plain table is enough; buy a service (LaunchDarkly, Unleash) only if targeting gets complex.
// Rule: delete a flag once the feature is fully rolled out, or flags become clutter.
// =============================================================================================
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string flag, CancellationToken ct);
    Task SetAsync(string flag, bool enabled, CancellationToken ct);
}

public static class FeatureFlagNames
{
    /// <summary>Q2 change 7: restaurants can apply to join the platform themselves.</summary>
    public const string SelfServiceOnboarding = "self-service-onboarding";
}

public sealed class PostgresFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    private readonly string _connectionString = configuration.ConnectionStringFor("Default");

    public async Task<bool> IsEnabledAsync(string flag, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT enabled FROM platform.feature_flags WHERE name = @name", connection);
        cmd.Parameters.AddWithValue("name", flag);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task SetAsync(string flag, bool enabled, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO platform.feature_flags (name, enabled, updated_at) VALUES (@name, @enabled, now())
            ON CONFLICT (name) DO UPDATE SET enabled = excluded.enabled, updated_at = now()
            """, connection);
        cmd.Parameters.AddWithValue("name", flag);
        cmd.Parameters.AddWithValue("enabled", enabled);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Tables owned by the platform plumbing itself (not by any business module).</summary>
public static class PlatformSchema
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS platform;
        CREATE TABLE IF NOT EXISTS platform.feature_flags (
            name        text PRIMARY KEY,
            enabled     boolean NOT NULL,
            updated_at  timestamptz NOT NULL
        );
        """ + JobQueue.SchemaSql;
}
