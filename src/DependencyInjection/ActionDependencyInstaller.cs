namespace Macaron.DependencyInjection;

public sealed class ActionDependencyInstaller(Action<IDependencyRegistrar> configure) : IDependencyInstaller
{
    public void Install(IDependencyRegistrar registrar)
    {
        configure.Invoke(registrar);
    }
}
