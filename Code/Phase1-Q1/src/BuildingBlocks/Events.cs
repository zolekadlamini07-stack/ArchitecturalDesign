using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// A DOMAIN EVENT is a fact, in the past tense: "OrderAccepted", "DeliveryCompleted".
/// The module that owns the fact publishes it; any module that cares subscribes.
/// The publisher does not know (or care) who is listening.
///
/// Why we have events in Q1 (D5): without them, Ordering calls Delivery ("request a driver")
/// and Delivery would have to call Ordering back ("delivered") - two modules depending on each
/// other in a circle. With events, Delivery just announces "DeliveryCompleted" and Ordering listens.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Base record so each event gets an id and timestamp for free.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Implemented by a module that wants to react to another module's event.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>Publishes events to subscribers.</summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent;
}

/// <summary>
/// Q1 IMPLEMENTATION: IN-PROCESS (in-memory) event bus.
/// "publish" simply finds every registered handler in this same process and awaits it, right now,
/// inside the same HTTP request. No broker, no queue, no infrastructure.
///
/// KNOWN WEAKNESS (accepted in Q1, fixed in Q2): if the process crashes after the publisher
/// saved its data but before the handlers ran, the event is LOST - it only ever existed in memory.
/// Q2 replaces this with a durable outbox + PostgreSQL job queue.
/// </summary>
public sealed class InProcessEventBus(IServiceProvider services) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, ct);
        }
    }
}
