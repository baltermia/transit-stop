using Microsoft.Extensions.DependencyInjection;

namespace TransitStop.Internal;

/// <summary>
/// Keeps the app's service collection so consumers and sagas registered with MassTransit can be
/// found later. By the time it is read the collection is complete (and read-only).
/// </summary>
internal sealed class RegisteredServices(IServiceCollection services)
{
	public IEnumerable<Type> Types()
	{
		foreach (ServiceDescriptor d in services.ToArray())
		{
			yield return d.ServiceType;

			if (d.ImplementationType is not null)
				yield return d.ImplementationType;
			if (d.ImplementationInstance is not null)
				yield return d.ImplementationInstance.GetType();
		}
	}
}
