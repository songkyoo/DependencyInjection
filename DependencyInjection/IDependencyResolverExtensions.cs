namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    public static T Resolve<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return (T)dependencyResolver.Resolve(key.Type, key.Tag);
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver)
    {
        return (T)dependencyResolver.Resolve(type: typeof(T), tag: "");
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "");
    }
}
