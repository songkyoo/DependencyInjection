namespace Macaron.DependencyInjection;

public static class DependencyContainerExtensions
{
    public static DependencyContainer CreateScopedContainer(
        this DependencyContainer dependencyContainer,
        Action<DependencyContainerBuilder>? configure = null
    )
    {
        var builder = new DependencyContainerBuilder();
        configure?.Invoke(builder);

        return builder.Build(dependencyContainer);
    }
}
