using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Notifications;

// =============================================================================================
// Q2 CHANGE 2 - NOTIFICATIONS LEAVE THE REQUEST PATH (problem P3, D12/D13)
//
// Q1: Ordering CALLED INotificationsApi.NotifyAsync() inside PlaceOrder/AcceptOrder. The customer
//     waited ~300 ms extra for the push provider (more when it was slow).
// Q2: Notifications SUBSCRIBES to Ordering's events. Ordering writes the event to its outbox and
//     returns immediately. The Worker turns the event into a job and runs the handlers below.
//
// Notice the dependency FLIPPED: Q1 Ordering -> Notifications; Q2 Notifications -> Ordering's
// PublicApi events. Ordering no longer knows notifications exist at all. Notifications now has
// no public API of its own (nobody calls it) - it only listens.
// =============================================================================================
public sealed class NotificationsModule : IModule
{
    public string Name => "notifications";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(NotificationsModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());
        services.AddSingleton<INotifier, LoggingNotifier>();
        services.AddScoped<NotificationSender>();

        // Subscriptions: each one becomes a background JOB per event (with retries + dead letter).
        // Q3: the restaurant hears about an order only once its money is HELD (OrderAwaitingAcceptance),
        // never at OrderPlaced. "Some restaurants receive orders, some don't" (Friday) can't happen.
        services.AddEventHandler<OrderAwaitingAcceptance, NotifyRestaurantOfNewOrder>();
        services.AddEventHandler<OrderPaymentFailed, NotifyCustomerPaymentFailed>();
        services.AddEventHandler<OrderAccepted, NotifyCustomerOrderAccepted>();
        services.AddEventHandler<OrderRejected, NotifyCustomerOrderRejected>();
        services.AddEventHandler<OrderDelivered, NotifyCustomerOrderDelivered>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

// ---------------------------------------------------------------------------------------------
// Event handler slices - one per event. Each is tiny: decide WHO and WHAT, then send.
// ---------------------------------------------------------------------------------------------
[JobQueue(JobQueues.Notifications)]
internal sealed class NotifyRestaurantOfNewOrder(NotificationSender sender) : IDomainEventHandler<OrderAwaitingAcceptance>
{
    public Task HandleAsync(OrderAwaitingAcceptance e, CancellationToken ct) =>
        e.Fulfilment == Fulfilment.Partner
            ? Task.CompletedTask // partner restaurants are told by THEIR system (the order is forwarded to it)
            : sender.SendOnceAsync(e.EventId, $"Restaurant:{e.RestaurantId}", "New order", $"Order {e.OrderId} is paid and waiting for you to accept it.", ct);
}

/// <summary>Q3: honest, calm wording - nobody is left guessing whether they were charged.</summary>
[JobQueue(JobQueues.Notifications)]
internal sealed class NotifyCustomerPaymentFailed(NotificationSender sender) : IDomainEventHandler<OrderPaymentFailed>
{
    public Task HandleAsync(OrderPaymentFailed e, CancellationToken ct) =>
        sender.SendOnceAsync(e.EventId, $"Customer:{e.CustomerId}", "Payment not taken",
            "We couldn't confirm your payment, so your order was not placed. You have not been charged; any temporary hold will be released automatically.", ct);
}

[JobQueue(JobQueues.Notifications)]
internal sealed class NotifyCustomerOrderAccepted(NotificationSender sender) : IDomainEventHandler<OrderAccepted>
{
    public Task HandleAsync(OrderAccepted e, CancellationToken ct) =>
        sender.SendOnceAsync(e.EventId, $"Customer:{e.CustomerId}", "Order accepted", "The restaurant accepted your order.", ct);
}

[JobQueue(JobQueues.Notifications)]
internal sealed class NotifyCustomerOrderRejected(NotificationSender sender) : IDomainEventHandler<OrderRejected>
{
    public Task HandleAsync(OrderRejected e, CancellationToken ct) =>
        sender.SendOnceAsync(e.EventId, $"Customer:{e.CustomerId}", "Order not accepted",
            $"The restaurant could not take your order ({e.Reason}). You have not been charged.", ct);
}

[JobQueue(JobQueues.Notifications)]
internal sealed class NotifyCustomerOrderDelivered(NotificationSender sender) : IDomainEventHandler<OrderDelivered>
{
    public Task HandleAsync(OrderDelivered e, CancellationToken ct) =>
        sender.SendOnceAsync(e.EventId, $"Customer:{e.CustomerId}", "Delivered", "Enjoy your meal!", ct);
}

/// <summary>
/// Sends a notification AT MOST ONCE per (event, recipient), even if the job runs again.
/// If the provider throws, the exception bubbles up and the JobRunner retries with backoff.
/// </summary>
internal sealed class NotificationSender(INotifier notifier, NotificationsDbContext db)
{
    public async Task SendOnceAsync(Guid eventId, string recipient, string subject, string message, CancellationToken ct)
    {
        if (await db.Log.AnyAsync(l => l.EventId == eventId && l.Recipient == recipient, ct)) return; // already sent

        await notifier.SendAsync(recipient, subject, message, ct);
        db.Log.Add(new NotificationLogEntry
        {
            Id = Guid.NewGuid(), EventId = eventId, Recipient = recipient, Subject = subject, Message = message, SentAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------
internal sealed class NotificationLogEntry
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
}

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<NotificationLogEntry> Log => Set<NotificationLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("notifications");
        b.Entity<NotificationLogEntry>().ToTable("notification_log");
    }
}

// ---------------------------------------------------------------------------------------------
// PORT + ADAPTER for the push/email provider (unchanged from Q1).
// ---------------------------------------------------------------------------------------------
internal interface INotifier
{
    Task SendAsync(string recipient, string subject, string message, CancellationToken ct);
}

/// <summary>Still takes ~300 ms - but now inside the WORKER, where no customer is waiting.</summary>
internal sealed class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    public async Task SendAsync(string recipient, string subject, string message, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        logger.LogInformation("NOTIFY {Recipient}: {Subject} - {Message}", recipient, subject, message);
    }
}
