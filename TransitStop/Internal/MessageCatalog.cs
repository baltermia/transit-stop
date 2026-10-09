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
	JsonNode? Sample);

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

			messages.Add(new MessageInfo(
				type,
				TypeNames.Id(type),
				TypeNames.Short(type),
				type.Namespace ?? "",
				KindOf(type),
				consumers.TryGetValue(type, out SortedSet<string>? names) ? [.. names] : [],
				new SampleGenerator().Create(type)));
		}

		messages.Sort((a, b) =>
		{
			int byNamespace = string.CompareOrdinal(a.Namespace, b.Namespace);
			return byNamespace != 0 ? byNamespace : string.CompareOrdinal(a.Name, b.Name);
		});

		return new MessageCatalog(messages);
	}

	static IEnumerable<Type> ConsumedMessages(Type type)
	{
		if (type.IsInterface || type.ContainsGenericParameters)
			yield break;

		foreach (Type contract in type.GetInterfaces())
		{
			if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IConsumer<>))
				yield return contract.GetGenericArguments()[0];
		}
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

		return !typeof(IConsumer).IsAssignableFrom(type);
	}

	static string KindOf(Type type)
	{
		if (type.IsInterface)
			return "interface";

		// the compiler emits this clone method for every record
		return type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null ? "record" : "class";
	}
}
