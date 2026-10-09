using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Payments.Data;

internal static class PaymentStatus
{
    public const string Authorised = "AUTHORISED";
    public const string Declined = "DECLINED";
    public const string Failed = "FAILED";
    public const string Captured = "CAPTURED";
    public const string Voided = "VOIDED";
}

internal sealed class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ProviderPaymentId { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("payments");
        b.Entity<Payment>().ToTable("payments");
    }
}
