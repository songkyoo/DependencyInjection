namespace Macaron.DependencyInjection;

public interface IDependencyResolver
{
    object Resolve(IKey key);

    bool Contains(IKey key);
}
