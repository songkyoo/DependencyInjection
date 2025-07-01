namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    public static object? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Type type, string tag)
    {
        return dependencyResolver.Contains(type, tag) ? dependencyResolver.Resolve(type, tag) : null;
    }

    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        string tag,
        Func<IDependencyResolver, object> factory
    )
    {
        return dependencyResolver.Contains(type, tag)
            ? dependencyResolver.Resolve(type, tag)
            : factory.Invoke(dependencyResolver);
    }

    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        string tag,
        Func<object> factory
    )
    {
        return dependencyResolver.Contains(type, tag) ? dependencyResolver.Resolve(type, tag) : factory.Invoke();
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return (T)dependencyResolver.Resolve(key.Type, key.Tag);
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Key<T> key)
        where T : class
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T?)dependencyResolver.Resolve(key.Type, key.Tag)
            : null;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T)dependencyResolver.Resolve(key.Type, key.Tag)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T)dependencyResolver.Resolve(key.Type, key.Tag)
            : factory.Invoke();
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver)
    {
        return (T)dependencyResolver.Resolve(type: typeof(T), tag: "");
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver)
        where T : class
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "")
            ? (T?)dependencyResolver.Resolve(type: typeof(T), tag: "")
            : null;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "")
            ? (T)dependencyResolver.Resolve(type: typeof(T), tag: "")
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "")
            ? (T)dependencyResolver.Resolve(type: typeof(T), tag: "")
            : factory.Invoke();
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return dependencyResolver.Contains(type: key.Type, tag: key.Tag);
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Contains(type: typeof(T), tag: "");
    }
}
