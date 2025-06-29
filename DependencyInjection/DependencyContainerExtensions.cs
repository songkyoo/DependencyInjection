namespace Macaron.DependencyInjection;

public static class DependencyContainerExtensions
{
    public static DependencyContainer CreateScope(
        this DependencyContainer dependencyContainer,
        Action<IDependencyRegistrar>? configure = null
    )
    {
        var builder = new DependencyContainerBuilder();
        configure?.Invoke(builder);

        return builder.Build(dependencyContainer);
    }
}
