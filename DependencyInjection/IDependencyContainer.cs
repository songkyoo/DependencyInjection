namespace Macaron.DependencyInjection;

public interface IDependencyContainer : IDependencyResolver, IDisposable
{
    IDependencyContainer CreateScope(IDependencyInstaller installer);

    LifetimeScope CreateLifetimeScope();
}
