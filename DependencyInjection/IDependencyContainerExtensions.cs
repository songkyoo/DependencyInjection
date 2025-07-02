namespace Macaron.DependencyInjection;

public static class IDependencyContainerExtensions
{
    public static IDependencyContainer CreateScope(this IDependencyContainer dependencyContainer)
    {
        return dependencyContainer.CreateScope(configure: null);
    }
}
