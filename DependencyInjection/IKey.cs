namespace Macaron.DependencyInjection;

public interface IKey : IEquatable<IKey>
{
    Type Type { get; }

    object? Tag { get; }
}
