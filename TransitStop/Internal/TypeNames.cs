namespace TransitStop.Internal;

internal static class TypeNames
{
	/// <summary>"Fault&lt;OrderSubmitted&gt;", "Outer.Inner"</summary>
	public static string Short(Type type) => Format(type, fullArguments: false);

	/// <summary>Unique, readable id: namespace plus name, generic arguments fully qualified.</summary>
	public static string Id(Type type)
	{
		string name = Format(type, fullArguments: true);
		return string.IsNullOrEmpty(type.Namespace) ? name : type.Namespace + "." + name;
	}

	static string Format(Type type, bool fullArguments)
	{
		string name = type.Name;
		int tick = name.IndexOf('`');
		if (tick >= 0)
			name = name[..tick];

		if (type.IsGenericType)
		{
			IEnumerable<string> args = type.GetGenericArguments().Select(a => fullArguments ? Id(a) : Short(a));
			name += "<" + string.Join(", ", args) + ">";
		}

		if (type.IsNested && !type.IsGenericParameter && type.DeclaringType is { } declaring)
			name = Format(declaring, fullArguments) + "." + name;

		return name;
	}
}
