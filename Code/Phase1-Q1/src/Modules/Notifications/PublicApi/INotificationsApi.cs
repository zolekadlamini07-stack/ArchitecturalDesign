// ============================================================================================
// PUBLIC API of the Notifications module.
// Other modules say WHO to tell and WHAT happened; this module decides HOW (push, email...).
// Only this module knows a push/email provider exists (behind its own port + adapter).
// ============================================================================================
namespace FoodDelivery.Modules.Notifications.PublicApi;

public interface INotificationsApi
{
    /// <summary>
    /// Q1: SYNCHRONOUS. The caller WAITS while the message is sent to the provider.
    /// That is fine for one restaurant, but in Q2 it is exactly what slows down PlaceOrder and
    /// AcceptOrder (problem P3), and it moves to a background worker.
    /// </summary>
    Task NotifyAsync(NotificationRecipient recipient, string subject, string message, CancellationToken ct);
}

/// <summary>Who to notify: a single user, or "whoever is on duty at this restaurant".</summary>
public sealed record NotificationRecipient(RecipientKind Kind, Guid Id)
{
    public static NotificationRecipient Customer(Guid customerId) => new(RecipientKind.Customer, customerId);
    public static NotificationRecipient Restaurant(Guid restaurantId) => new(RecipientKind.Restaurant, restaurantId);
    public static NotificationRecipient Driver(Guid driverId) => new(RecipientKind.Driver, driverId);
    public override string ToString() => $"{Kind}:{Id}";
}

public enum RecipientKind { Customer, Restaurant, Driver }
