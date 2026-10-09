using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.BuildingBlocks;

// =============================================================================================
// TRANSACTIONAL OUTBOX (Q2, D12)
//
// The DUAL-WRITE PROBLEM it solves:
//     save the order in the database      OK
//     --- process crashes here ---
//     tell the notification system        NEVER HAPPENS -> restaurant never hears about the order
//
// The fix: write the event as a ROW in an "outbox" table, in the SAME database transaction as the
// business data. Either both are saved or neither is. A background process (OutboxDispatcher, in
// the Worker) reads the outbox later and turns each event into jobs.
// Analogy: write the appointment AND the "text my friend" note on the same page, in one go.
//
// Each module that publishes events has its OWN outbox table, inside its OWN schema
// (e.g. ordering.outbox), so data ownership rules still hold.
// =============================================================================================

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    /// <summary>The event's .NET type, so the dispatcher can turn the JSON back into an object.</summary>
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public static class OutboxExtensions
{
    /// <summary>Maps the outbox table inside the module's own schema. Call from OnModelCreating.</summary>
    public static void AddOutbox(this ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox");
        outbox.Property(m => m.Payload).HasColumnType("jsonb"); // stored as real JSON, queryable for support
    }

    /// <summary>
    /// "Publish" an event the Q2 way: stage it in the outbox. It is written when the slice calls
    /// SaveChangesAsync - in the SAME transaction as the slice's own changes.
    /// </summary>
    public static void AddToOutbox(this DbContext db, IDomainEvent domainEvent) =>
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = domainEvent.EventId,
            Type = domainEvent.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
            OccurredAt = domainEvent.OccurredAt,
        });

    /// <summary>The SQL every publishing module adds to its schema.sql (shown here for reference).</summary>
    public const string TableSqlTemplate = """
        CREATE TABLE IF NOT EXISTS {schema}.outbox (
            id            uuid PRIMARY KEY,
            type          text NOT NULL,
            payload       jsonb NOT NULL,
            occurred_at   timestamptz NOT NULL,
            processed_at  timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_outbox_unprocessed ON {schema}.outbox (occurred_at) WHERE processed_at IS NULL;
        """;
}

/// <summary>The list of module schemas that have an outbox the dispatcher must read.</summary>
public sealed class OutboxRegistry
{
    private readonly List<string> _schemas = [];
    public IReadOnlyList<string> Schemas => _schemas;
    internal void Add(string schema) { if (!_schemas.Contains(schema)) _schemas.Add(schema); }
}

public static class OutboxRegistration
{
    /// <summary>Declare that this module publishes events through its outbox.</summary>
    public static IServiceCollection AddOutboxFor(this IServiceCollection services, string schema)
    {
        EventRegistration.Singleton<OutboxRegistry>(services).Add(schema);
        return services;
    }
}
