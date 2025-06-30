namespace Macaron.DependencyInjection;

public interface IDependencyResolver
{
    object Resolve(Type type, string tag);

    bool Contains(Type type, string tag);
}
