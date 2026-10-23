using System.Runtime.CompilerServices;

namespace Macaron.DependencyInjection;

internal sealed class KeyReferenceComparer : IEqualityComparer<object>
{
    public static KeyReferenceComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(object obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
