namespace Macaron.DependencyInjection;

public interface IDependencyResolver
{
    T Resolve<T>(Key<T> key);

    bool Contains<T>(Key<T> key);
}
