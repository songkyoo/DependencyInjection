using System.Collections.Immutable;

namespace Macaron.DependencyInjection.Generator;

internal sealed class FactoryDefinitionsComparer : IEqualityComparer<ImmutableArray<FactoryDefinition>>
{
    #region Static
    internal static FactoryDefinitionsComparer Instance { get; } = new();
    #endregion

    #region Constructors
    private FactoryDefinitionsComparer()
    {
    }
    #endregion

    #region IEqualityComparer<ImmutableArray<FactoryDefinition>> Interface
    public bool Equals(ImmutableArray<FactoryDefinition> x, ImmutableArray<FactoryDefinition> y)
    {
        return x.SequenceEqual(y);
    }

    public int GetHashCode(ImmutableArray<FactoryDefinition> definitions)
    {
        var hash = 17;

        foreach (var definition in definitions)
        {
            hash = unchecked(hash * 31 + definition.GetHashCode());
        }

        return hash;
    }
    #endregion
}
