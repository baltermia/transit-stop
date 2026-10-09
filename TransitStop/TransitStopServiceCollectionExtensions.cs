using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitStop;
using TransitStop.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers TransitStop.
/// </summary>
public static class TransitStopServiceCollectionExtensions
{
	/// <summary>
	/// Adds TransitStop. Then call <c>app.MapTransitStop()</c> to serve the UI.
	/// </summary>
	public static IServiceCollection AddTransitStop(this IServiceCollection services, Action<TransitStopOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		if (services.Any(d => d.ServiceType == typeof(TransitStopOptions)))
			throw new InvalidOperationException("AddTransitStop() was called more than once.");

		TransitStopOptions options = new();
		configure?.Invoke(options);

		services.AddSingleton(options);

		// the collection is complete by the time the catalog is built (first request), which is
		// how consumers registered after this call are still discovered
		services.AddSingleton(new RegisteredServices(services));
		services.TryAddSingleton<TransitStopRuntime>();

		return services;
	}
}
