// ============================================================================================
// PUBLIC API of the Customers module.
// This interface is the "serving hatch" between Customers and the rest of the system.
// Ordering needs a delivery address when an order is placed, so it asks HERE - it never reads
// the customers.addresses table itself (that table is locked inside this module, D4).
// ============================================================================================
namespace FoodDelivery.Modules.Customers.PublicApi;

public interface ICustomersApi
{
    /// <summary>Returns the customer's saved address, or null if it doesn't exist / isn't theirs.</summary>
    Task<DeliveryAddress?> GetDeliveryAddressAsync(Guid customerId, Guid addressId, CancellationToken ct);
}

/// <summary>
/// What the outside world is allowed to know about an address. Deliberately a separate, small
/// record (not the internal database entity), so we can change our tables without breaking callers.
/// </summary>
public sealed record DeliveryAddress(Guid AddressId, string Line1, string City, string PostalCode)
{
    public override string ToString() => $"{Line1}, {City}, {PostalCode}";
}
