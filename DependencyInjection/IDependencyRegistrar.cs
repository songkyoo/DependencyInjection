namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void RegisterTransient(Type type, string tag, Func<IDependencyContainer, object> factory);

    void RegisterScoped(Type type, string tag, Func<IDependencyContainer, object> factory);

    void RegisterSingleton(Type type, string tag, Func<IDependencyContainer, object> factory, Ownership ownership);
}
