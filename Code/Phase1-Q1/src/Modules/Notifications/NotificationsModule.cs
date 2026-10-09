using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Notifications.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => "notifications";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(NotificationsModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());
        services.AddSingleton<INotifier, LoggingNotifier>(); // swap for an FCM/email adapter in production
        services.AddScoped<INotificationsApi, SendNotification>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------
internal sealed class NotificationLogEntry
{
    public Guid Id { get; set; }
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
// PORT + ADAPTER for the push/email provider (same idea as Payments' provider).
// ---------------------------------------------------------------------------------------------
internal interface INotifier
{
    Task SendAsync(string recipient, string subject, string message, CancellationToken ct);
}

/// <summary>
/// Development adapter: writes the notification to the log instead of a phone.
/// The 300 ms delay imitates a real push provider's latency, so you can SEE the Q2 problem:
/// every request that notifies someone gets 300 ms slower.
/// </summary>
internal sealed class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    public async Task SendAsync(string recipient, string subject, string message, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        logger.LogInformation("NOTIFY {Recipient}: {Subject} - {Message}", recipient, subject, message);
    }
}

// ---------------------------------------------------------------------------------------------
// SLICE: "Send a notification" - reached through the public API.
// ---------------------------------------------------------------------------------------------
internal sealed class SendNotification(INotifier notifier, NotificationsDbContext db, ILogger<SendNotification> logger) : INotificationsApi
{
    public async Task NotifyAsync(NotificationRecipient recipient, string subject, string message, CancellationToken ct)
    {
        try
        {
            await notifier.SendAsync(recipient.ToString(), subject, message, ct);
            db.Log.Add(new NotificationLogEntry
            {
                Id = Guid.NewGuid(), Recipient = recipient.ToString(), Subject = subject, Message = message, SentAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // A failed notification must NEVER undo the main action (the order is already saved).
            // Screens poll for status, so the user still sees the update - nothing is lost.
            logger.LogWarning(ex, "Notification to {Recipient} failed; continuing.", recipient);
        }
    }
}
