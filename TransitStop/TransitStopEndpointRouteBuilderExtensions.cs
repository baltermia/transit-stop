using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TransitStop.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps the TransitStop UI into an existing ASP.NET Core app.
/// </summary>
public static class TransitStopEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Serves the TransitStop UI at <paramref name="pattern"/>. Maps nothing when TransitStop is
	/// disabled for the current environment (see <c>TransitStopOptions.EnableWhen</c>).
	/// The returned builder accepts conventions such as <c>RequireAuthorization()</c>.
	/// </summary>
	public static IEndpointConventionBuilder MapTransitStop(this IEndpointRouteBuilder endpoints, string pattern = "/transit-stop")
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		TransitStopRuntime runtime = endpoints.ServiceProvider.GetService<TransitStopRuntime>()
			?? throw new InvalidOperationException("Call services.AddTransitStop() before app.MapTransitStop().");

		RouteGroupBuilder group = endpoints.MapGroup(pattern);
		group.ExcludeFromDescription();

		if (!runtime.IsEnabled)
		{
			runtime.LogDisabled();
			return group;
		}

		group.MapGet("", context => runtime.ServePage(context));
		group.MapGet("api/messages", context => runtime.ListMessages(context));
		group.MapPost("api/publish", context => runtime.Dispatch(context, send: false));
		group.MapPost("api/send", context => runtime.Dispatch(context, send: true));
		runtime.LogMapped(pattern);

		return group;
	}
}
