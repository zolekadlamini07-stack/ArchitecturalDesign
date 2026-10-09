using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// A DOMAIN EVENT is a fact, in the past tense: "OrderAccepted", "DeliveryCompleted".
/// The module that owns the fact publishes it; any module that cares subscribes.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Implemented by a module that reacts to another module's event.
///
/// Q2 CHANGE: handlers now run in the WORKER process, as jobs, possibly MORE THAN ONCE
/// (at-least-once delivery). So every handler must be IDEMPOTENT: running it twice must have the
/// same effect as running it once (check first, or rely on a unique constraint).
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

// =============================================================================================
// Q2 CHANGE (D12): the Q1 InProcessEventBus is GONE.
//
// Q1: slice saves data -> calls events.PublishAsync() -> handlers run in memory, right now.
//     Problem: a crash between the save and the handlers LOSES the event, and slow handlers
//     (notifications!) make the user's request slow.
// Q2: slice writes the event into its OUTBOX table in the SAME transaction as its data
//     (see Outbox.cs). The worker process turns outbox rows into jobs and runs the handlers later.
//     Crash-safe, retryable, and the user's request no longer waits.
// =============================================================================================

/// <summary>Which handler listens to which event. Built at startup from the modules' registrations.</summary>
public sealed class EventSubscriptions
{
    private readonly List<(Type Event, Type Handler)> _subscriptions = [];
    public IReadOnlyList<(Type Event, Type Handler)> All => _subscriptions;
    internal void Add(Type eventType, Type handlerType) => _subscriptions.Add((eventType, handlerType));
    public IEnumerable<Type> HandlersFor(Type eventType) => _subscriptions.Where(s => s.Event == eventType).Select(s => s.Handler);
}

public static class EventRegistration
{
    /// <summary>Subscribe a handler to an event. Used in each module's AddServices.</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        Singleton<EventSubscriptions>(services).Add(typeof(TEvent), typeof(THandler));
        return services;
    }

    internal static T Singleton<T>(IServiceCollection services) where T : class, new()
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(T))?.ImplementationInstance as T;
        if (existing is not null) return existing;
        var created = new T();
        services.AddSingleton(created);
        return created;
    }
}
