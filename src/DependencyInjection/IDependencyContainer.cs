namespace Macaron.DependencyInjection;

public interface IDependencyContainer : IDependencyResolver, IDisposable
{
    IDependencyContainer CreateChild();

    IDependencyContainer CreateChild(IDependencyInstaller installer);

    LocalScope CreateLocalScope();
}
