namespace Macaron.DependencyInjection;

public interface IDependencyInstaller
{
    void Install(IDependencyRegistrar registrar);
}
