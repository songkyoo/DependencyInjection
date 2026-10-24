namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient<T>(
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterScoped<T>(
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterSingleton<T>(
        DependencyFactory<T> factory,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;
}
