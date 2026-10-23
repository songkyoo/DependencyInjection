namespace Macaron.DependencyInjection;

public static class IDependencyResolverValueTypeExtensions
{
    #region ResolveOrNull
    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Key<T> key)
        where T : struct
    {
        return dependencyResolver.Contains(key)
            ? dependencyResolver.Resolve(key)
            : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver)
        where T : struct
    {
        return dependencyResolver.Contains(Key<T>.Default)
            ? dependencyResolver.Resolve(Key<T>.Default)
            : null;
    }
    #endregion
}
