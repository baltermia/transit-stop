using TransitStop;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers TransitStop.
/// </summary>
public static class TransitStopServiceCollectionExtensions
{
	/// <summary>
	/// Adds TransitStop.
	/// </summary>
	public static IServiceCollection AddTransitStop(this IServiceCollection services, Action<TransitStopOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		TransitStopOptions options = new();
		configure?.Invoke(options);

		services.AddSingleton(options);

		return services;
	}
}
