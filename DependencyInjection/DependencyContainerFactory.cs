namespace Macaron.DependencyInjection;

public static class DependencyContainerFactory
{
    public static IDependencyContainer Create(Action<IDependencyRegistrar>? configure = null)
    {
        var builder = new DependencyContainerBuilder();

        configure?.Invoke(builder);

        return builder.Build();
    }
}
