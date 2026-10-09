using FoodDelivery.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FoodDelivery.Modules.PartnerIntegration.Data;

/// <summary>
/// Data access for the integration module (plain SQL - small, explicit tables).
/// Owns the ID MAP: the one place where "their store 8812" becomes "our restaurant 4f2a...".
/// </summary>
internal sealed class IntegrationStore(IConfiguration configuration)
{
    private readonly NpgsqlDataSource _db = NpgsqlDataSource.Create(configuration.ConnectionStringFor("partner_integration"));

    /// <summary>Our id for one of their ids - created the first time we see it, stable forever after.</summary>
    public async Task<Guid> OurIdAsync(string entityType, string theirId, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("""
            INSERT INTO partner_integration.id_map (entity_type, their_id, our_id) VALUES (@t, @their, @ours)
            ON CONFLICT (entity_type, their_id) DO UPDATE SET their_id = excluded.their_id
            RETURNING our_id
            """);
        cmd.Parameters.AddWithValue("t", entityType);
        cmd.Parameters.AddWithValue("their", theirId);
        cmd.Parameters.AddWithValue("ours", Guid.NewGuid());
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<string?> TheirIdAsync(string entityType, Guid ourId, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("SELECT their_id FROM partner_integration.id_map WHERE entity_type = @t AND our_id = @ours");
        cmd.Parameters.AddWithValue("t", entityType);
        cmd.Parameters.AddWithValue("ours", ourId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<bool> AlreadyForwardedAsync(Guid ourOrderId, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("SELECT 1 FROM partner_integration.forwarded_orders WHERE our_order_id = @id");
        cmd.Parameters.AddWithValue("id", ourOrderId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    public async Task RecordForwardedAsync(Guid ourOrderId, string theirOrderNo, string status, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("""
            INSERT INTO partner_integration.forwarded_orders (our_order_id, their_order_no, their_status, forwarded_at)
            VALUES (@id, @no, @s, now()) ON CONFLICT (our_order_id) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", ourOrderId);
        cmd.Parameters.AddWithValue("no", theirOrderNo);
        cmd.Parameters.AddWithValue("s", status);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public NpgsqlDataSource DataSource => _db;
}
