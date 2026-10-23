namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    #region Resolve
    public static T Resolve<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Resolve(Key<T>.Default);
    }
    #endregion

    #region ResolveOrNull
    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Key<T> key)
        where T : class
    {
        return dependencyResolver.Contains(key)
            ? dependencyResolver.Resolve(key)
            : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver)
        where T : class
    {
        return dependencyResolver.Contains(Key<T>.Default)
            ? dependencyResolver.Resolve(Key<T>.Default)
            : null;
    }
    #endregion

    #region ResolveOrElse Factory
    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(key)
            ? dependencyResolver.Resolve(key)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(key)
            ? dependencyResolver.Resolve(key)
            : factory.Invoke();
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(Key<T>.Default)
            ? dependencyResolver.Resolve(Key<T>.Default)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(Key<T>.Default)
            ? dependencyResolver.Resolve(Key<T>.Default)
            : factory.Invoke();
    }
    #endregion

    #region ResolveOrElse Default
    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        T defaultValue
    )
    {
        return dependencyResolver.Contains(key)
            ? dependencyResolver.Resolve(key)
            : defaultValue;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        T defaultValue
    )
    {
        return dependencyResolver.Contains(Key<T>.Default)
            ? dependencyResolver.Resolve(Key<T>.Default)
            : defaultValue;
    }
    #endregion

    #region Contains
    public static bool Contains<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Contains(Key<T>.Default);
    }
    #endregion
}
