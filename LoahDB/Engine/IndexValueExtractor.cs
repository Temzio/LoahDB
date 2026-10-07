using System.Collections;
using System.Reflection;

namespace LoahDB.Engine;

internal static class IndexValueExtractor
{
    public static IEnumerable<object?> GetKeyValues(object document, IReadOnlyList<string> propertyPaths)
    {
        var components = propertyPaths
            .Select(path => GetPropertyValue(document, path))
            .ToList();
        if (components.Count == 0)
        {
            yield return null;
            yield break;
        }

        if (components.Count > 1)
        {
            yield return components;
            yield break;
        }

        var single = components[0];
        if (single is string or null)
        {
            yield return single;
            yield break;
        }

        if (single is IEnumerable enumerable and not byte[])
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }

            yield break;
        }

        yield return single;
    }

    public static object? GetPropertyValue(object document, string propertyPath)
    {
        object? current = document;
        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                throw new InvalidOperationException($"Property '{segment}' was not found on '{current.GetType().Name}'.");
            }

            current = property.GetValue(current);
        }

        return current;
    }
}
