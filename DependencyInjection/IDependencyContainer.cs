namespace Macaron.DependencyInjection;

public interface IDependencyContainer : IDependencyResolver, IDisposable
{
    IDependencyContainer CreateScope(Action<IDependencyRegistrar>? configure);

    void AddDisposable(IDisposable disposable);
}
