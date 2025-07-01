namespace Macaron.DependencyInjection;

public static class IDependencyResolverValueTypeExtensions
{
    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Key<T> key)
        where T : struct
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T?)dependencyResolver.Resolve(key.Type, key.Tag)
            : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver)
        where T : struct
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "")
            ? (T?)dependencyResolver.Resolve(type: typeof(T), tag: "")
            : null;
    }
}
