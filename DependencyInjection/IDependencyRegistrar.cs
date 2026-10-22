namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient(
        Type type,
        object? tag,
        Func<IDependencyContainer, object> factory,
        Action<object>? cleanup = null
    );

    void RegisterScoped(
        Type type,
        object? tag,
        Func<IDependencyContainer, object> factory,
        Action<object>? cleanup = null
    );

    void RegisterSingleton(
        Type type,
        object? tag,
        Func<IDependencyContainer, object> factory,
        Action<object>? cleanup = null
    );
}
