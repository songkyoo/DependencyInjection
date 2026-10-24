namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    #region ResolveOrNull
    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, object? tag = null)
        where T : class
    {
        return dependencyResolver.Contains<T>(tag)
            ? dependencyResolver.Resolve<T>(tag)
            : null;
    }
    #endregion

    #region ResolveOrElse Factory
    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<IDependencyResolver, T> factory,
        object? tag = null
    )
    {
        return dependencyResolver.Contains<T>(tag)
            ? dependencyResolver.Resolve<T>(tag)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<T> factory,
        object? tag = null
    )
    {
        return dependencyResolver.Contains<T>(tag)
            ? dependencyResolver.Resolve<T>(tag)
            : factory.Invoke();
    }
    #endregion

    #region ResolveOrElse Default
    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        T defaultValue,
        object? tag = null
    )
    {
        return dependencyResolver.Contains<T>(tag)
            ? dependencyResolver.Resolve<T>(tag)
            : defaultValue;
    }
    #endregion
}
