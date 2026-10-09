using System.Text.Json;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.PartnerIntegration.Adapter;
using FoodDelivery.Modules.PartnerIntegration.Data;
using FoodDelivery.Modules.PartnerIntegration.Translation;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.PartnerIntegration.Features.SyncPartnerCatalogue;

// ============================================================================================
// CHALLENGE FLOW 4.1 - "Their restaurants appear on our platform" (D27: SYNCED COPY, not live calls)
//
//   1. fetch their stores + menus (THEIR model)       - via the adapter, behind a circuit breaker
//   2. map their ids to ours (id_map)                  - created on first sight, stable after
//   3. TRANSLATE to our model                          - cents -> Money, in_stock -> IsAvailable...
//   4. store a READ-ONLY copy via Restaurants' PUBLIC API (source = PARTNER)
//
// Our browse path NEVER calls their API live, so their outage can't slow our customers down.
// If syncing fails for > 30 minutes, their restaurants are shown as closed ("temporarily
// unavailable") from the last good copy - so we never take orders we can't forward.
// ============================================================================================
internal sealed class CatalogueSync(IPartnerPlatform partner, IntegrationStore store, IRestaurantsApi restaurants, ILogger<CatalogueSync> logger)
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    public async Task<int> SyncOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<TheirStore> stores;
        try
        {
            stores = await partner.GetStoresAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Partner catalogue sync failed ({Error}); serving the last good copy.", ex.Message);
            await MarkUnavailableIfStaleAsync(ct);
            return 0;
        }

        foreach (var theirStore in stores)
            await UpsertAsync(theirStore, theirStore.AcceptingOrders, ct);

        await using var save = store.DataSource.CreateCommand("""
            INSERT INTO partner_integration.sync_state (name, last_success_at, last_snapshot) VALUES ('catalogue', now(), @snap::jsonb)
            ON CONFLICT (name) DO UPDATE SET last_success_at = now(), last_snapshot = excluded.last_snapshot
            """);
        save.Parameters.AddWithValue("snap", JsonSerializer.Serialize(stores));
        await save.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Partner catalogue synced: {Count} store(s).", stores.Count);
        return stores.Count;
    }

    private async Task UpsertAsync(TheirStore theirStore, bool isOpen, CancellationToken ct)
    {
        var ourRestaurantId = await store.OurIdAsync("restaurant", theirStore.StoreId.ToString(), ct);
        var itemIds = new Dictionary<string, Guid>();
        foreach (var sku in theirStore.Sections.SelectMany(s => s.Products).Select(p => p.Sku))
            itemIds[sku] = await store.OurIdAsync("menu_item", sku, ct);

        var snapshot = PartnerTranslator.ToOurRestaurant(theirStore with { AcceptingOrders = isOpen }, ourRestaurantId, sku => itemIds[sku]);
        await restaurants.UpsertPartnerRestaurantAsync(snapshot, ct);
    }

    private async Task MarkUnavailableIfStaleAsync(CancellationToken ct)
    {
        await using var cmd = store.DataSource.CreateCommand(
            "SELECT last_success_at, last_snapshot::text FROM partner_integration.sync_state WHERE name = 'catalogue'");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return;
        if (DateTimeOffset.UtcNow - r.GetFieldValue<DateTimeOffset>(0) < StaleAfter) return;

        var lastGood = JsonSerializer.Deserialize<List<TheirStore>>(r.GetString(1)) ?? [];
        await r.DisposeAsync();
        foreach (var theirStore in lastGood) await UpsertAsync(theirStore, isOpen: false, ct);
        logger.LogWarning("Partner catalogue stale for over {Minutes} min - their restaurants marked temporarily unavailable.", StaleAfter.TotalMinutes);
    }
}

/// <summary>WORKER job: sync every minute here (every few minutes in production) - plus their "menu changed" webhook would trigger it.</summary>
internal sealed class CatalogueSyncJob(IServiceProvider services, ILogger<CatalogueSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CatalogueSync>().SyncOnceAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Catalogue sync round failed."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }
}

/// <summary>Admin endpoint: "sync now".</summary>
internal static class TriggerCatalogueSync
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/integration/partner/sync", async (CatalogueSync sync, CancellationToken ct) => Results.Ok(new { stores = await sync.SyncOnceAsync(ct) }))
           .RequireAuthorization(p => p.RequireRole(Roles.Admin)).WithTags("Integration");
}
