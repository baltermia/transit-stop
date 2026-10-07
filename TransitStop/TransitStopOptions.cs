namespace TransitStop;

/// <summary>
/// Configures which message types TransitStop shows and how it dispatches them.
/// </summary>
public sealed class TransitStopOptions
{
	internal List<Type> ExplicitTypes { get; } = [];

	/// <summary>Shown in the page header and the browser tab. Defaults to the application name.</summary>
	public string? Title { get; set; }

	/// <summary>Lists <typeparamref name="T"/>.</summary>
	public TransitStopOptions AddMessage<T>() where T : class => AddMessage(typeof(T));

	/// <summary>Lists <paramref name="type"/>.</summary>
	public TransitStopOptions AddMessage(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		ExplicitTypes.Add(type);
		return this;
	}
}
