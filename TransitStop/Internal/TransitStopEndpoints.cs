using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TransitStop.Internal;

internal static class TransitStopEndpoints
{
	public static void Map(IEndpointRouteBuilder routes, TransitStopRuntime runtime)
	{
		routes.MapGet("", context => runtime.ServePage(context));
		routes.MapGet("api/messages", context => runtime.ListMessages(context));
		routes.MapPost("api/publish", context => runtime.Dispatch(context, send: false));
		routes.MapPost("api/send", context => runtime.Dispatch(context, send: true));
	}
}
