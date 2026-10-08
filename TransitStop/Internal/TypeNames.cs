namespace TransitStop.Internal;

internal static class TypeNames
{
	public static string Short(Type type) => type.Name;

	public static string Id(Type type) => type.FullName ?? type.Name;
}
