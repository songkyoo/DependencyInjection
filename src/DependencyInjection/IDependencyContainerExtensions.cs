namespace Macaron.DependencyInjection;

public static class IDependencyContainerExtensions
{
    public static IDependencyContainer CreateChild(
        this IDependencyContainer dependencyContainer,
        Action<IDependencyRegistrar> configure
    )
    {
        return dependencyContainer.CreateChild(new ActionDependencyInstaller(configure));
    }
}
