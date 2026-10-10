using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TransitStop.Internal;

/// <summary>
/// Builds an example JSON payload for a message type from its properties, so the editor starts
/// with every field already in place. Enum types encountered along the way are collected so the
/// UI can show their allowed values.
/// </summary>
internal sealed class SampleGenerator(JsonNamingPolicy? namingPolicy)
{
	const int MaxDepth = 8;

	static readonly string Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

	readonly HashSet<Type> path = [];
	readonly SortedDictionary<string, string[]> enums = new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, string[]> Enums => enums;

	public JsonNode? Create(Type type) => Value(type, 0);

	JsonNode? Value(Type type, int depth)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;

		if (type == typeof(string)) return "string";
		if (type == typeof(bool)) return false;
		if (type == typeof(char)) return "a";
		if (type == typeof(Guid)) return Guid.NewGuid().ToString();
		if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return Now;
		if (type == typeof(DateOnly)) return Now[..10];
		if (type == typeof(TimeOnly)) return "00:00:00";
		if (type == typeof(TimeSpan)) return "00:00:00";
		if (type == typeof(Uri)) return "https://example.com";
		if (type == typeof(byte[])) return "";
		if (IsNumber(type)) return 0;

		if (type.IsEnum)
		{
			string[] names = Enum.GetNames(type);
			enums[TypeNames.Short(type)] = names;
			return names.Length > 0 ? names[0] : (JsonNode)0;
		}

		if (type == typeof(object) || type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type))
			return new JsonObject();

		// MessageData<T> is a reference to an external payload, not something to type in
		if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "MassTransit.MessageData`1")
			return null;

		if (depth >= MaxDepth || !path.Add(type))
			return null;

		try
		{
			if (DictionaryValueType(type) is { } valueType)
				return new JsonObject { ["key"] = Value(valueType, depth + 1) };

			if (typeof(IEnumerable).IsAssignableFrom(type))
				return ElementType(type) is { } elementType ? new JsonArray(Value(elementType, depth + 1)) : new JsonArray();

			if (type.IsPrimitive || type.IsPointer || type == typeof(Type))
				return null;

			JsonObject obj = new();
			foreach (PropertyInfo property in Properties(type))
				obj[PropertyName(property)] = Value(property.PropertyType, depth + 1);
			return obj;
		}
		finally
		{
			path.Remove(type);
		}
	}

	static IEnumerable<PropertyInfo> Properties(Type type)
	{
		if (type.IsInterface)
		{
			// interface properties are spread over the interface and everything it extends
			HashSet<string> seen = new(StringComparer.Ordinal);
			return new[] { type }.Concat(type.GetInterfaces())
				.SelectMany(i => i.GetProperties())
				.Where(p => p.GetIndexParameters().Length == 0 && !IsIgnored(p) && seen.Add(p.Name));
		}

		HashSet<string> constructorParameters = new(
			type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name ?? ""),
			StringComparer.OrdinalIgnoreCase);

		// get-only properties are computed unless a constructor can set them
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanRead
				&& p.GetIndexParameters().Length == 0
				&& (p.CanWrite || constructorParameters.Contains(p.Name))
				&& !IsIgnored(p));
	}

	string PropertyName(PropertyInfo property)
	{
		if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } stj)
			return stj.Name;

		// Newtonsoft's [JsonProperty("name")], read by name so there is no dependency on it
		foreach (CustomAttributeData attribute in property.CustomAttributes)
		{
			if (attribute.AttributeType.FullName != "Newtonsoft.Json.JsonPropertyAttribute")
				continue;

			if (attribute.ConstructorArguments is [{ Value: string positional }])
				return positional;
			if (attribute.NamedArguments.FirstOrDefault(a => a.MemberName == "PropertyName").TypedValue.Value is string named)
				return named;
		}

		return namingPolicy?.ConvertName(property.Name) ?? property.Name;
	}

	static bool IsIgnored(PropertyInfo property)
	{
		foreach (CustomAttributeData attribute in property.CustomAttributes)
		{
			switch (attribute.AttributeType.FullName)
			{
				case "Newtonsoft.Json.JsonIgnoreAttribute":
				case "System.Runtime.Serialization.IgnoreDataMemberAttribute":
					return true;
				case "System.Text.Json.Serialization.JsonIgnoreAttribute":
					// only Condition = Always (the default) skips the property when reading
					CustomAttributeNamedArgument condition = attribute.NamedArguments.FirstOrDefault(a => a.MemberName == "Condition");
					return condition.MemberInfo is null || Equals(condition.TypedValue.Value, (int)JsonIgnoreCondition.Always);
			}
		}

		return false;
	}

	static Type? DictionaryValueType(Type type)
	{
		foreach (Type candidate in new[] { type }.Concat(type.GetInterfaces()))
		{
			if (!candidate.IsGenericType)
				continue;

			Type definition = candidate.GetGenericTypeDefinition();
			if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
				return candidate.GetGenericArguments()[1];
		}

		return null;
	}

	static Type? ElementType(Type type)
	{
		if (type.IsArray)
			return type.GetElementType();

		return new[] { type }.Concat(type.GetInterfaces())
			.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			?.GetGenericArguments()[0];
	}

	static bool IsNumber(Type type) =>
		type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
		|| type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)
		|| type == typeof(double) || type == typeof(float) || type == typeof(decimal);
}
