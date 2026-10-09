using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Customers.Features.Addresses;

/// <summary>SLICE: "Add a delivery address" (needed before placing an order).</summary>
internal static class AddAddress
{
    public sealed record Request(string Line1, string City, string PostalCode, bool IsDefault);
    public sealed record Response(Guid AddressId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers/me/addresses", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Customers");

    private static async Task<IResult> Handle(Request request, ClaimsPrincipal user, CustomersDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Line1) || string.IsNullOrWhiteSpace(request.City))
            return Problems.Validation(nameof(request.Line1), "Address line and city are required.");

        var customerId = user.UserId();

        // Q2 - EVENTUAL CONSISTENCY: the profile is created by a background job a moment after
        // sign-up. If the customer is faster than the worker, create the profile here instead.
        // The job handler checks before inserting, so whichever runs second simply does nothing.
        if (!await db.Profiles.AnyAsync(p => p.CustomerId == customerId, ct))
        {
            db.Profiles.Add(new CustomerProfile { CustomerId = customerId, DisplayName = user.Identity?.Name ?? "", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        if (request.IsDefault)
            await db.Addresses.Where(a => a.CustomerId == customerId)
                              .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);

        var address = new Address
        {
            Id = Guid.NewGuid(), CustomerId = customerId, Line1 = request.Line1.Trim(),
            City = request.City.Trim(), PostalCode = request.PostalCode.Trim(), IsDefault = request.IsDefault,
        };
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/customers/me/addresses/{address.Id}", new Response(address.Id));
    }
}

/// <summary>SLICE: "List my addresses". A plain query straight to a response - no domain model needed.</summary>
internal static class ListAddresses
{
    public sealed record Item(Guid AddressId, string Line1, string City, string PostalCode, bool IsDefault);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/customers/me/addresses", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Customers");

    private static async Task<IResult> Handle(ClaimsPrincipal user, CustomersDbContext db, CancellationToken ct)
    {
        var customerId = user.UserId();
        var items = await db.Addresses.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefault)
            .Select(a => new Item(a.Id, a.Line1, a.City, a.PostalCode, a.IsDefault))
            .ToListAsync(ct);
        return Results.Ok(items);
    }
}
