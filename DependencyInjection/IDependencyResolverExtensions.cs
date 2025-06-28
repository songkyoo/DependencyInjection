namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    public static T Resolve<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return (T)dependencyResolver.Resolve(key);
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver)
    {
        return (T)dependencyResolver.Resolve(Key.Of<T>());
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Contains(Key.Of<T>());
    }
}
