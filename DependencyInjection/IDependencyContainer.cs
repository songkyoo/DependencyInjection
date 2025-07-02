namespace Macaron.DependencyInjection;

public interface IDependencyContainer : IDependencyResolver
{
    IDependencyContainer CreateScope(Action<IDependencyRegistrar>? configure);
}
