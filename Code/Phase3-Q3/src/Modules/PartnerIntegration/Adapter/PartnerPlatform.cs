using System.Text.Json.Serialization;

namespace FoodDelivery.Modules.PartnerIntegration.Adapter;

// ============================================================================================
// THEIR MODEL - the acquired company's shapes, field names and status codes, exactly as their API
// sends them. Notice how different they are from ours (integer ids, prices in cents, "sections",
// "DISPATCHED"...). These types are 'internal' and live ONLY in this folder: our other modules
// never see them. That is the whole point of an anti-corruption layer.
// ============================================================================================
internal sealed record TheirStore(
    [property: JsonPropertyName("store_id")] int StoreId,
    [property: JsonPropertyName("store_name")] string StoreName,
    [property: JsonPropertyName("street")] string Street,
    [property: JsonPropertyName("town")] string Town,
    [property: JsonPropertyName("accepting_orders")] bool AcceptingOrders,
    [property: JsonPropertyName("delivery_cents")] int DeliveryCents,
    [property: JsonPropertyName("sections")] IReadOnlyList<TheirSection> Sections);

internal sealed record TheirSection(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("products")] IReadOnlyList<TheirProduct> Products);

internal sealed record TheirProduct(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("price_cents")] int PriceCents,
    [property: JsonPropertyName("in_stock")] int? InStock);   // 0/1, and sometimes missing!

internal sealed record TheirOrderRequest(
    [property: JsonPropertyName("store_id")] int StoreId,
    [property: JsonPropertyName("external_ref")] string ExternalRef,   // OUR order id -> makes retries safe
    [property: JsonPropertyName("drop_off")] string DropOff,
    [property: JsonPropertyName("items")] IReadOnlyList<TheirOrderItem> Items);

internal sealed record TheirOrderItem([property: JsonPropertyName("sku")] string Sku, [property: JsonPropertyName("qty")] int Qty);

internal sealed record TheirOrderCreated([property: JsonPropertyName("order_no")] string OrderNo, [property: JsonPropertyName("status")] string Status);

// ============================================================================================
// PORT: what WE need from their platform. Real adapter = HTTP client against their API with a
// PINNED version (e.g. /v2/) and a tolerant reader (ignore fields we don't use). Fake adapter below.
// ============================================================================================
internal interface IPartnerPlatform
{
    Task<IReadOnlyList<TheirStore>> GetStoresAsync(CancellationToken ct);

    /// <summary>Idempotent: the same external_ref returns the same order_no and never creates a second order.</summary>
    Task<TheirOrderCreated> CreateOrderAsync(TheirOrderRequest request, CancellationToken ct);
}

internal sealed class PartnerUnavailableException(string message) : Exception(message);
