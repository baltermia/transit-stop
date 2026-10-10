using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace TransitStop;

/// <summary>
/// Configures which message types TransitStop shows and how it dispatches them.
/// </summary>
public sealed class TransitStopOptions
{
	internal List<Type> ExplicitTypes { get; } = [];
	internal List<(Assembly Assembly, Func<Type, bool> Filter)> AssemblySources { get; } = [];

	/// <summary>Shown in the page header and the browser tab. Defaults to the application name.</summary>
	public string? Title { get; set; }

	/// <summary>
	/// When true (default), every message type consumed by a consumer, job consumer or saga
	/// registered in this app's MassTransit configuration is listed automatically.
	/// </summary>
	public bool DiscoverConsumedMessages { get; set; } = true;

	/// <summary>
	/// Decides whether TransitStop is active. Defaults to the Development environment only, so
	/// forgetting to remove it does not expose a "publish anything" endpoint in production.
	/// </summary>
	public Func<IHostEnvironment, bool> EnableWhen { get; set; } = env => env.IsDevelopment();

	/// <summary>
	/// Turns the edited JSON into a message object. Defaults to MassTransit's System.Text.Json
	/// settings (plus string enums). Replace it when the bus uses a different serializer,
	/// e.g. Newtonsoft with custom converters.
	/// </summary>
	public Func<string, Type, object?>? Deserializer { get; set; }

	/// <summary>Property naming used for the generated sample payloads. Defaults to camelCase.</summary>
	public JsonNamingPolicy? SampleNamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

	/// <summary>Lists <typeparamref name="T"/>.</summary>
	public TransitStopOptions AddMessage<T>() where T : class => AddMessage(typeof(T));

	/// <summary>Lists <paramref name="type"/>.</summary>
	public TransitStopOptions AddMessage(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		ExplicitTypes.Add(type);
		return this;
	}

	/// <summary>Lists all message-like types of the assembly containing <typeparamref name="T"/>.</summary>
	public TransitStopOptions AddMessagesFromAssemblyContaining<T>(Func<Type, bool>? filter = null) =>
		AddMessagesFromAssembly(typeof(T).Assembly, filter);

	/// <summary>
	/// Lists all message-like types of <paramref name="assembly"/>: public, non-abstract classes,
	/// records and interfaces that are not consumers or sagas.
	/// </summary>
	public TransitStopOptions AddMessagesFromAssembly(Assembly assembly, Func<Type, bool>? filter = null)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		AssemblySources.Add((assembly, filter ?? (_ => true)));
		return this;
	}
}
