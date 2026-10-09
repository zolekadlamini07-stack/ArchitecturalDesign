using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Identity module (events).
// Everything in a "PublicApi" folder is the module's contract with the rest of the system.
// Other modules may SUBSCRIBE to these events. They may NOT touch anything else in this project
// (the compiler stops them: every other type here is 'internal').
// ============================================================================================
namespace FoodDelivery.Modules.Identity.PublicApi;

/// <summary>
/// Raised when a customer signs up. The Customers module listens and creates an empty profile.
/// Identity does not know the Customers module exists - it just announces the fact.
/// </summary>
public sealed record CustomerRegistered(Guid UserId, string Email, string DisplayName) : DomainEvent;

/// <summary>
/// Raised when restaurant staff create a driver account. The Delivery module listens and creates
/// the driver's profile, linked to that restaurant (D7: drivers belong to restaurants).
/// </summary>
public sealed record DriverAccountCreated(Guid UserId, Guid RestaurantId, string DisplayName) : DomainEvent;
