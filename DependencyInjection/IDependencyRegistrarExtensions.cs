namespace Macaron.DependencyInjection;

public static class IDependencyRegistrarExtensions
{
    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        DependencyFactory<T> factory,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        switch (lifetime)
        {
            case Lifetime.Transient:
            {
                dependencyRegistrar.RegisterTransient(factory, tag, cleanup);

                break;
            }
            case Lifetime.Scoped:
            {
                dependencyRegistrar.RegisterScoped(factory, tag, cleanup);

                break;
            }
            case Lifetime.Singleton:
            {
                dependencyRegistrar.RegisterSingleton(factory, tag, cleanup);

                break;
            }
            default:
                throw new InvalidOperationException($"Not supported life time: {lifetime}");
        }
    }

    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register<T>(
            factory: (_, ref _) => factory.Invoke(),
            lifetime,
            tag,
            cleanup
        );
    }

    #region Transient
    public static void RegisterTransient<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register<T>(
            factory: (_, ref _) => factory.Invoke(),
            lifetime: Lifetime.Transient,
            tag,
            cleanup
        );
    }

    #endregion

    #region Scoped
    public static void RegisterScoped<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register<T>(
            factory: (_, ref _) => factory.Invoke(),
            lifetime: Lifetime.Scoped,
            tag,
            cleanup
        );
    }

    #endregion

    #region Singleton
    public static void RegisterSingleton<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register<T>(
            factory: (_, ref _) => factory.Invoke(),
            lifetime: Lifetime.Singleton,
            tag,
            cleanup
        );
    }

    #endregion

    #region Instance
    public static void RegisterSingleton<T>(
        this IDependencyRegistrar dependencyRegistrar,
        T instance,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        if (instance is null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        dependencyRegistrar.Register<T>(
            factory: (_, ref _) => instance,
            lifetime: Lifetime.Singleton,
            tag,
            cleanup
        );
    }
    #endregion
}
