namespace Macaron.DependencyInjection;

public static partial class DependencyContainerBuilderExtensions
{
    public static void Register<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        Key<T> key,
        Func<T> factory,
        LifeTime lifeTime
    ) where T : notnull
    {
        dependencyContainerBuilder.Register(key, factory: _ => factory.Invoke(), lifeTime);
    }

    public static void Register<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        Func<IDependencyResolver, T> factory,
        LifeTime lifeTime
    ) where T : notnull
    {
        dependencyContainerBuilder.Register(Key.Of<T>(), factory, lifeTime);
    }

    public static void Register<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        Func<T> factory,
        LifeTime lifeTime
    ) where T : notnull
    {
        dependencyContainerBuilder.Register(Key.Of<T>(), factory: _ => factory.Invoke(), lifeTime);
    }

    public static void Register<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        Key<T> key,
        LifeTime lifeTime
    ) where T : notnull, new()
    {
        dependencyContainerBuilder.Register(key, factory: _ => new T(), lifeTime);
    }

    public static void Register<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        LifeTime lifeTime
    ) where T : notnull, new()
    {
        dependencyContainerBuilder.Register(Key.Of<T>(), factory: _ => new T(), lifeTime);
    }

    public static void RegisterSingleton<T>(
        this DependencyContainerBuilder dependencyContainerBuilder,
        Key<T> key,
        T instance
    ) where T : notnull
    {
        dependencyContainerBuilder.Register(key, factory: _ => instance, LifeTime.Singleton);
    }
}
