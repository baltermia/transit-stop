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

			// the non-keyed accessors throw on keyed descriptors
			Type? implementation = d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;
			object? instance = d.IsKeyedService ? d.KeyedImplementationInstance : d.ImplementationInstance;

			if (implementation is not null)
				yield return implementation;
			if (instance is not null)
				yield return instance.GetType();
		}
	}
}
