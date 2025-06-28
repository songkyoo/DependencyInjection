namespace Macaron.DependencyInjection;

public interface IKey : IEquatable<IKey>
{
    Type Type { get; }

    string Tag { get; }
}
