using Microsoft.AspNetCore.Http;

namespace FoodDelivery.BuildingBlocks;

/// <summary>Small helpers so every slice returns errors in the same shape (RFC 7807 problem details).</summary>
public static class Problems
{
    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult NotFound(string what) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{what} not found.");

    public static IResult Conflict(string message) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: message);

    public static IResult Forbidden(string message) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: message);
}

/// <summary>A page of results. History lists are paginated so they stay cheap as data grows.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize);
