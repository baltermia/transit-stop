namespace TransitStop.Demo.Contracts;

public enum ShippingSpeed
{
	Standard,
	Express,
	Overnight,
}

public record OrderLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>Command: consumed by <see cref="SubmitOrderConsumer"/>, which publishes <see cref="OrderSubmitted"/>.</summary>
public record SubmitOrder(
	Guid OrderId,
	string CustomerEmail,
	ShippingSpeed Shipping,
	List<OrderLine> Lines,
	Dictionary<string, string>? Notes);

/// <summary>Event: starts the order saga.</summary>
public record OrderSubmitted(Guid OrderId, string CustomerEmail, decimal Total, DateTime SubmittedAt);

/// <summary>Event as an interface: completes the order saga, which then sends <see cref="NotifyCustomer"/>.</summary>
public interface OrderShipped
{
	Guid OrderId { get; }
	string TrackingNumber { get; }
}

/// <summary>Command sent to the "notify-customer" queue.</summary>
public record NotifyCustomer(string Email, string Subject, string Body);

/// <summary>Consumed in batches by <see cref="SensorReadingsConsumer"/>.</summary>
public record SensorReading(string SensorId, double Value, DateTimeOffset MeasuredAt);
