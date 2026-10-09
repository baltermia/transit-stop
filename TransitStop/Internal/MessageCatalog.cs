using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MassTransit;

namespace TransitStop.Internal;

internal sealed record MessageInfo(
	Type Type,
	string Id,
	string Name,
	string Namespace,
	string Kind,
	IReadOnlyList<string> Consumers,
	JsonNode? Sample,
	IReadOnlyDictionary<string, string[]> Enums);

internal sealed class MessageCatalog
{
	readonly Dictionary<string, MessageInfo> byId;

	MessageCatalog(IReadOnlyList<MessageInfo> messages)
	{
		Messages = messages;
		byId = messages.ToDictionary(m => m.Id, StringComparer.Ordinal);
	}

	public IReadOnlyList<MessageInfo> Messages { get; }

	public bool TryGet(string id, out MessageInfo message) => byId.TryGetValue(id, out message!);

	public static MessageCatalog Build(TransitStopOptions options, RegisteredServices registered)
	{
		HashSet<Type> types = [.. options.ExplicitTypes];

		foreach ((Assembly assembly, Func<Type, bool> filter) in options.AssemblySources)
		{
			foreach (Type type in assembly.GetTypes())
			{
				if (IsMessageCandidate(type) && filter(type))
					types.Add(type);
			}
		}

		Dictionary<Type, SortedSet<string>> consumers = [];

		if (options.DiscoverConsumedMessages)
		{
			HashSet<Type> scanned = [];
			foreach (Type candidate in registered.Types())
			{
				if (!scanned.Add(candidate))
					continue;

				foreach (Type message in ConsumedMessages(candidate))
				{
					types.Add(message);

					if (!consumers.TryGetValue(message, out SortedSet<string>? names))
						consumers[message] = names = new SortedSet<string>(StringComparer.Ordinal);
					names.Add(TypeNames.Short(candidate));
				}
			}
		}

		List<MessageInfo> messages = [];
		foreach (Type type in types)
		{
			if (type.ContainsGenericParameters)
				continue;

			SampleGenerator samples = new();
			JsonNode? sample = samples.Create(type);

			messages.Add(new MessageInfo(
				type,
				TypeNames.Id(type),
				TypeNames.Short(type),
				type.Namespace ?? "",
				KindOf(type),
				consumers.TryGetValue(type, out SortedSet<string>? names) ? [.. names] : [],
				sample,
				samples.Enums));
		}

		messages.Sort((a, b) =>
		{
			int byNamespace = string.CompareOrdinal(a.Namespace, b.Namespace);
			return byNamespace != 0 ? byNamespace : string.CompareOrdinal(a.Name, b.Name);
		});

		// two distinct types can only share an id when they live in different assemblies
		List<MessageInfo> unique = messages.GroupBy(m => m.Id).Select(g => g.First()).ToList();
		return new MessageCatalog(unique);
	}

	static IEnumerable<Type> ConsumedMessages(Type type)
	{
		if (type.IsInterface || type.ContainsGenericParameters || IsFromMassTransit(type))
			yield break;

		foreach (Type contract in type.GetInterfaces())
		{
			if (!contract.IsGenericType)
				continue;

			Type definition = contract.GetGenericTypeDefinition();
			if (definition == typeof(IConsumer<>)
				|| definition == typeof(IJobConsumer<>)
				|| definition == typeof(InitiatedBy<>)
				|| definition == typeof(Orchestrates<>)
				|| definition == typeof(InitiatedByOrOrchestrates<>)
				|| definition == typeof(Observes<,>))
			{
				yield return UnwrapBatch(contract.GetGenericArguments()[0]);
			}
		}

		if (!IsStateMachine(type))
			yield break;

		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			Type propertyType = property.PropertyType;
			if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Event<>))
				yield return UnwrapBatch(propertyType.GetGenericArguments()[0]);
		}
	}

	static Type UnwrapBatch(Type message) =>
		message.IsGenericType && message.GetGenericTypeDefinition() == typeof(Batch<>)
			? message.GetGenericArguments()[0]
			: message;

	static bool IsStateMachine(Type type)
	{
		for (Type? t = type.BaseType; t is not null; t = t.BaseType)
		{
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(MassTransitStateMachine<>))
				return true;
		}

		return false;
	}

	static bool IsMessageCandidate(Type type)
	{
		if (!type.IsPublic
			|| type.ContainsGenericParameters
			|| type.IsDefined(typeof(CompilerGeneratedAttribute), false))
		{
			return false;
		}

		if (!type.IsInterface && !(type.IsClass && !type.IsAbstract))
			return false;

		if (typeof(IConsumer).IsAssignableFrom(type)
			|| typeof(ISaga).IsAssignableFrom(type))
		{
			return false;
		}

		// state machines, consumer/saga definitions, ... - messages don't derive from MassTransit classes
		for (Type? t = type.BaseType; t is not null; t = t.BaseType)
		{
			if (IsFromMassTransit(t))
				return false;
		}

		return true;
	}

	static bool IsFromMassTransit(Type type) =>
		type.Assembly.GetName().Name?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false;

	static string KindOf(Type type)
	{
		if (type.IsInterface)
			return "interface";

		// the compiler emits this clone method for every record
		return type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null ? "record" : "class";
	}
}
