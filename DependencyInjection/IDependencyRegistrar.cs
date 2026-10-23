namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterScoped<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterSingleton<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup = null
    ) where T : notnull;
}
