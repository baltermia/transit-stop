namespace TransitStop.Demo.Contracts;

public record OrderLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>Command: consumed by <see cref="SubmitOrderConsumer"/>, which publishes <see cref="OrderSubmitted"/>.</summary>
public record SubmitOrder(
	Guid OrderId,
	string CustomerEmail,
	List<OrderLine> Lines,
	Dictionary<string, string>? Notes);

/// <summary>Event: nothing in this app consumes it.</summary>
public record OrderSubmitted(Guid OrderId, string CustomerEmail, decimal Total, DateTime SubmittedAt);
