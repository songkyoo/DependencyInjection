namespace Macaron.DependencyInjection;

public static class IDependencyResolverExtensions
{
    #region Resolve
    public static object Resolve(this IDependencyResolver dependencyResolver, Type type)
    {
        return dependencyResolver.Resolve(type, tag: null);
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return (T)dependencyResolver.Resolve(key.Type, key.Tag);
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver, object tag)
    {
        return (T)dependencyResolver.Resolve(typeof(T), tag);
    }

    public static T Resolve<T>(this IDependencyResolver dependencyResolver)
    {
        return (T)dependencyResolver.Resolve(typeof(T), tag: null);
    }
    #endregion

    #region ResolveOrNull
    public static object? ResolveOrNull(this IDependencyResolver dependencyResolver, Type type, object tag)
    {
        return dependencyResolver.Contains(type, tag) ? dependencyResolver.Resolve(type, tag) : null;
    }

    public static object? ResolveOrNull(this IDependencyResolver dependencyResolver, Type type)
    {
        return dependencyResolver.Contains(type, tag: null) ? dependencyResolver.Resolve(type, tag: null) : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, Key<T> key)
        where T : class
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T?)dependencyResolver.Resolve(key.Type, key.Tag)
            : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, object tag)
        where T : class
    {
        return dependencyResolver.Contains(typeof(T), tag)
            ? (T?)dependencyResolver.Resolve(typeof(T), tag)
            : null;
    }

    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver)
        where T : class
    {
        return dependencyResolver.Contains(typeof(T), tag: null)
            ? (T?)dependencyResolver.Resolve(typeof(T), tag: null)
            : null;
    }
    #endregion

    #region ResolveOrElse Factory
    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        object tag,
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
        object tag,
        Func<object> factory
    )
    {
        return dependencyResolver.Contains(type, tag) ? dependencyResolver.Resolve(type, tag) : factory.Invoke();
    }

    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        Func<IDependencyResolver, object> factory
    )
    {
        return dependencyResolver.Contains(type, tag: null)
            ? dependencyResolver.Resolve(type, tag: null)
            : factory.Invoke(dependencyResolver);
    }

    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        Func<object> factory
    )
    {
        return dependencyResolver.Contains(type, tag: null)
            ? dependencyResolver.Resolve(type, tag: null)
            : factory.Invoke();
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

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        object tag,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(typeof(T), tag)
            ? (T)dependencyResolver.Resolve(typeof(T), tag)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        object tag,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(typeof(T), tag)
            ? (T)dependencyResolver.Resolve(typeof(T), tag)
            : factory.Invoke();
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<IDependencyResolver, T> factory
    )
    {
        return dependencyResolver.Contains(typeof(T), tag: null)
            ? (T)dependencyResolver.Resolve(typeof(T), tag: null)
            : factory.Invoke(dependencyResolver);
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Func<T> factory
    )
    {
        return dependencyResolver.Contains(typeof(T), tag: null)
            ? (T)dependencyResolver.Resolve(typeof(T), tag: null)
            : factory.Invoke();
    }
    #endregion

    #region ResolveOrElse Default
    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        object tag,
        object defaultValue
    )
    {
        return dependencyResolver.Contains(type, tag) ? dependencyResolver.Resolve(type, tag) : defaultValue;
    }

    public static object ResolveOrElse(
        this IDependencyResolver dependencyResolver,
        Type type,
        object defaultValue
    )
    {
        return dependencyResolver.Contains(type, tag: null) ? dependencyResolver.Resolve(type, tag: null) : defaultValue;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        Key<T> key,
        T defaultValue
    )
    {
        return dependencyResolver.Contains(key.Type, key.Tag)
            ? (T)dependencyResolver.Resolve(key.Type, key.Tag)
            : defaultValue;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        object tag,
        T defaultValue
    )
    {
        return dependencyResolver.Contains(typeof(T), tag)
            ? (T)dependencyResolver.Resolve(typeof(T), tag)
            : defaultValue;
    }

    public static T ResolveOrElse<T>(
        this IDependencyResolver dependencyResolver,
        T defaultValue
    )
    {
        return dependencyResolver.Contains(typeof(T), tag: null)
            ? (T)dependencyResolver.Resolve(typeof(T), tag: null)
            : defaultValue;
    }
    #endregion

    #region Contains
    public static bool Contains(this IDependencyResolver dependencyResolver, Type type)
    {
        return dependencyResolver.Contains(type, tag: null);
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver, Key<T> key)
    {
        return dependencyResolver.Contains(key.Type, key.Tag);
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver, object tag)
    {
        return dependencyResolver.Contains(typeof(T), tag);
    }

    public static bool Contains<T>(this IDependencyResolver dependencyResolver)
    {
        return dependencyResolver.Contains(typeof(T), tag: null);
    }
    #endregion
}
