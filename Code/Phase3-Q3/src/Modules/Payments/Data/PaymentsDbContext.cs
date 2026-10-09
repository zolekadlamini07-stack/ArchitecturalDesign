using FoodDelivery.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Payments.Data;

/// <summary>
/// Q3 - PAYMENT ATTEMPT STATES. The key new one is UNKNOWN.
///
///   REQUESTED  -> IN_FLIGHT -> AUTHORISED -> CAPTURED          (happy path)
///                          -> DECLINED                         (a definite "no")
///                          -> UNKNOWN -> AUTHORISED | DECLINED (resolved by reconciliation)
///   AUTHORISED -> VOIDED                                       (rejected / order given up)
///
/// Over a network a payment is not success-or-failure. It can also be "we asked and never heard
/// back". UNKNOWN is never shown to anyone as "failed".
/// </summary>
internal static class AttemptStatus
{
    public const string Requested = "REQUESTED";
    public const string InFlight = "IN_FLIGHT";
    public const string Authorised = "AUTHORISED";
    public const string Declined = "DECLINED";
    public const string Unknown = "UNKNOWN";
    public const string Captured = "CAPTURED";
    public const string Voided = "VOIDED";
}

internal sealed class PaymentAttempt
{
    /// <summary>Our attempt id - and the IDEMPOTENCY KEY sent to the provider.</summary>
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string PaymentToken { get; set; } = "";
    public string Status { get; set; } = AttemptStatus.Requested;
    public string? ProviderPaymentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Money Money => new(Amount, Currency);
}

/// <summary>One line in the append-only payment log.</summary>
internal sealed class PaymentEventLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public Guid OrderId { get; set; }
    public string What { get; set; } = "";
    public string? Detail { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class WebhookInboxEntry
{
    public string ProviderEventId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

internal sealed class SettlementException
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AttemptId { get; set; }
    public string Reference { get; set; } = "";
    public string Problem { get; set; } = "";
    public DateTimeOffset FoundAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<PaymentAttempt> Attempts => Set<PaymentAttempt>();
    public DbSet<PaymentEventLog> Log => Set<PaymentEventLog>();
    public DbSet<WebhookInboxEntry> WebhookInbox => Set<WebhookInboxEntry>();
    public DbSet<SettlementException> SettlementExceptions => Set<SettlementException>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("payments");
        b.AddOutbox();
        b.Entity<PaymentAttempt>(a =>
        {
            a.ToTable("payment_attempts");
            a.Ignore(x => x.Money);
            // OPTIMISTIC CONCURRENCY: the job, the webhook processor and the sweeper can all learn about
            // the same attempt at the same moment. Every UPDATE includes "...AND status = <what I read>",
            // so if someone else changed it first, my save is REFUSED instead of overwriting the truth
            // (e.g. a late "timeout -> UNKNOWN" can never overwrite an "AUTHORISED" learned from a webhook).
            a.Property(x => x.Status).IsConcurrencyToken();
        });
        b.Entity<PaymentEventLog>().ToTable("payment_events");
        b.Entity<WebhookInboxEntry>().ToTable("provider_webhook_inbox").HasKey(w => w.ProviderEventId);
        b.Entity<SettlementException>().ToTable("settlement_exceptions");
    }

    /// <summary>Write a line to the audit log (saved with the next SaveChanges).</summary>
    public void Record(PaymentAttempt attempt, string what, string? detail = null) =>
        Log.Add(new PaymentEventLog { AttemptId = attempt.Id, OrderId = attempt.OrderId, What = what, Detail = detail });
}
