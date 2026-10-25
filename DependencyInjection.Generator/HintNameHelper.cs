using System.Text;

namespace Macaron.DependencyInjection.Generator;

internal static class HintNameHelper
{
    internal static string GetAssemblyHintName(string sourceName, string assemblyIdentity)
    {
        const uint fnvPrime = 16777619;
        const uint offsetBasis = 2166136261;

        var bytes = Encoding.UTF8.GetBytes($"{assemblyIdentity}, {sourceName}");
        var hash = offsetBasis;

        unchecked
        {
            foreach (var value in bytes)
            {
                hash ^= value;
                hash *= fnvPrime;
            }
        }

        return $"{sourceName}.{hash:x8}";
    }
}
