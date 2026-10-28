namespace Macaron.DependencyInjection;

public interface IDependencyContainer : IDependencyResolver, IDisposable
{
    IDependencyContainer CreateScope();

    IDependencyContainer CreateScope(IDependencyInstaller installer);

    TransientScope CreateTransientScope();
}
