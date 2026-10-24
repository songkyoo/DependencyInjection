namespace Macaron.DependencyInjection;

public interface IDependencyResolver
{
    T Resolve<T>(object? tag = null);

    bool Contains<T>(object? tag = null);
}
