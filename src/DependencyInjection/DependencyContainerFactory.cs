namespace Macaron.DependencyInjection;

public static class DependencyContainerFactory
{
    public static IDependencyContainer Create(IDependencyInstaller installer)
    {
        var builder = new DependencyContainerBuilder();

        installer.Install(builder);

        return builder.Build();
    }

    public static IDependencyContainer Create(Action<IDependencyRegistrar> configure)
    {
        return Create(new ActionDependencyInstaller(configure));
    }
}
