using System.Text;

namespace Macaron.DependencyInjection.Generator;

internal static class HintNameHelpers
{
    internal static string GetAssemblyHintName(string sourceName, string assemblyIdentity)
    {
        return $"{sourceName}.{GetStableHash($"{assemblyIdentity}, {sourceName}"):x8}";
    }

    internal static string GetCreationHintName(string name, int arity, string fullyQualifiedName)
    {
        return $"InjectionContextExtensions.{name}_{arity}.{GetStableHash(fullyQualifiedName):x8}.g.cs";
    }

    internal static string GetTagFieldPrefix(string name, int arity, string fullyQualifiedName)
    {
        return $"_tag_{name}_{arity}_{GetStableHash(fullyQualifiedName):x8}_";
    }

    private static uint GetStableHash(string value)
    {
        const uint fnvPrime = 16777619;
        const uint offsetBasis = 2166136261;

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = offsetBasis;

        unchecked
        {
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= fnvPrime;
            }
        }

        return hash;
    }
}
