namespace Macaron.DependencyInjection;

public static class IDependencyContainerExtensions
{
    public static IDependencyContainer CreateScope(
        this IDependencyContainer dependencyContainer,
        Action<IDependencyRegistrar> configure
    )
    {
        return dependencyContainer.CreateScope(new ActionDependencyInstaller(configure));
    }
}
