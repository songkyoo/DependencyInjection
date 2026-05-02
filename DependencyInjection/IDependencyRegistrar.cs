namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient(Type type, object? tag, Func<IDependencyContainer, object> factory);

    void RegisterScoped(Type type, object? tag, Func<IDependencyContainer, object> factory);

    void RegisterSingleton(Type type, object? tag, Func<IDependencyContainer, object> factory, Ownership ownership);
}
