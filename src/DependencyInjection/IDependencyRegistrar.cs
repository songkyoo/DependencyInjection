namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient<T>(
        RegistrationSource.Factory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterTransient<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterScoped<T>(
        RegistrationSource.Factory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterScoped<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterSingleton<T>(
        RegistrationSource.Instance<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterSingleton<T>(
        RegistrationSource.Factory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;

    void RegisterSingleton<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag = null,
        Action<T>? cleanup = null
    ) where T : notnull;
}
