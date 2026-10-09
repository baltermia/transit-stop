using MassTransit;
using TransitStop.Demo.Contracts;

namespace TransitStop.Demo;

public class OrderState : SagaStateMachineInstance
{
	public Guid CorrelationId { get; set; }
	public string? CurrentState { get; set; }
}

/// <summary>
/// Publish OrderSubmitted (or SubmitOrder) to start an order, then OrderShipped with the same
/// order id to finish it.
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
	public State Submitted { get; private set; } = null!;

	public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
	public Event<OrderShipped> OrderShipped { get; private set; } = null!;

	public OrderStateMachine(ILogger<OrderStateMachine> logger)
	{
		InstanceState(x => x.CurrentState);

		Event(() => OrderSubmitted, e => e.CorrelateById(m => m.Message.OrderId));
		Event(() => OrderShipped, e =>
		{
			e.CorrelateById(m => m.Message.OrderId);
			e.OnMissingInstance(m => m.Execute(c =>
				logger.LogWarning("OrderShipped for unknown order {OrderId} - submit it first", c.Message.OrderId)));
		});

		Initially(
			When(OrderSubmitted)
				.Then(c => logger.LogInformation("Saga: order {OrderId} submitted", c.Saga.CorrelationId))
				.TransitionTo(Submitted));

		During(Submitted,
			When(OrderShipped)
				.Then(c => logger.LogInformation("Saga: order {OrderId} shipped ({Tracking})", c.Saga.CorrelationId, c.Message.TrackingNumber))
				.Finalize());

		SetCompletedWhenFinalized();
	}
}
