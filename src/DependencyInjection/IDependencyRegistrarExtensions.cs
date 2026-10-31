namespace Macaron.DependencyInjection;

public static class IDependencyRegistrarExtensions
{
    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        RegistrationSource.Factory<T> source,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        switch (lifetime)
        {
            case Lifetime.Transient:
            {
                dependencyRegistrar.RegisterTransient(source, tag, cleanup);

                break;
            }
            case Lifetime.Scoped:
            {
                dependencyRegistrar.RegisterScoped(source, tag, cleanup);

                break;
            }
            case Lifetime.Singleton:
            {
                dependencyRegistrar.RegisterSingleton(source, tag, cleanup);

                break;
            }
            default:
                throw new InvalidOperationException($"Not supported life time: {lifetime}");
        }
    }

    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        RegistrationSource.ContextFactory<T> source,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        switch (lifetime)
        {
            case Lifetime.Transient:
            {
                dependencyRegistrar.RegisterTransient(source, tag, cleanup);

                break;
            }
            case Lifetime.Scoped:
            {
                dependencyRegistrar.RegisterScoped(source, tag, cleanup);

                break;
            }
            case Lifetime.Singleton:
            {
                dependencyRegistrar.RegisterSingleton(source, tag, cleanup);

                break;
            }
            default:
                throw new InvalidOperationException($"Not supported life time: {lifetime}");
        }
    }

    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        DependencyFactory<T> factory,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register(new RegistrationSource.ContextFactory<T>(factory), lifetime, tag, cleanup);
    }

    public static void Register<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        Lifetime lifetime,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.Register(new RegistrationSource.Factory<T>(factory), lifetime, tag, cleanup);
    }

    public static void RegisterTransient<T>(
        this IDependencyRegistrar dependencyRegistrar,
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterTransient(new RegistrationSource.ContextFactory<T>(factory), tag, cleanup);
    }

    public static void RegisterTransient<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterTransient(new RegistrationSource.Factory<T>(factory), tag, cleanup);
    }

    public static void RegisterScoped<T>(
        this IDependencyRegistrar dependencyRegistrar,
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterScoped(new RegistrationSource.ContextFactory<T>(factory), tag, cleanup);
    }

    public static void RegisterScoped<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterScoped(new RegistrationSource.Factory<T>(factory), tag, cleanup);
    }

    public static void RegisterSingleton<T>(
        this IDependencyRegistrar dependencyRegistrar,
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterSingleton(new RegistrationSource.ContextFactory<T>(factory), tag, cleanup);
    }

    public static void RegisterSingleton<T>(
        this IDependencyRegistrar dependencyRegistrar,
        Func<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterSingleton(new RegistrationSource.Factory<T>(factory), tag, cleanup);
    }

    public static void RegisterInstance<T>(
        this IDependencyRegistrar dependencyRegistrar,
        T instance,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull
    {
        dependencyRegistrar.RegisterSingleton(new RegistrationSource.Instance<T>(instance), tag, cleanup);
    }
}
