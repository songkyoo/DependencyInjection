namespace Macaron.DependencyInjection;

public interface IDependencyResolver
{
    object Resolve(Type type, object? tag);

    bool Contains(Type type, object? tag);
}
