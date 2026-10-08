using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TransitStop.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps TransitStop into an existing ASP.NET Core app.
/// </summary>
public static class TransitStopEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Serves the TransitStop API at <paramref name="pattern"/>.
	/// The returned builder accepts conventions such as <c>RequireAuthorization()</c>.
	/// </summary>
	public static IEndpointConventionBuilder MapTransitStop(this IEndpointRouteBuilder endpoints, string pattern = "/transit-stop")
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		TransitStopRuntime runtime = endpoints.ServiceProvider.GetService<TransitStopRuntime>()
			?? throw new InvalidOperationException("Call services.AddTransitStop() before app.MapTransitStop().");

		RouteGroupBuilder group = endpoints.MapGroup(pattern);
		group.ExcludeFromDescription();

		group.MapGet("api/messages", context => runtime.ListMessages(context));
		runtime.LogMapped(pattern);

		return group;
	}
}
