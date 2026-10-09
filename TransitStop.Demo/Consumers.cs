using MassTransit;
using TransitStop.Demo.Contracts;

namespace TransitStop.Demo;

public class SubmitOrderConsumer(ILogger<SubmitOrderConsumer> logger) : IConsumer<SubmitOrder>
{
	public async Task Consume(ConsumeContext<SubmitOrder> context)
	{
		SubmitOrder order = context.Message;
		decimal total = order.Lines?.Sum(l => l.Quantity * l.UnitPrice) ?? 0;

		logger.LogInformation("Order {OrderId} from {Customer}: {Lines} line(s), {Total:0.00} total",
			order.OrderId, order.CustomerEmail, order.Lines?.Count ?? 0, total);

		await context.Publish(new OrderSubmitted(order.OrderId, order.CustomerEmail, total, DateTime.UtcNow));
	}
}
