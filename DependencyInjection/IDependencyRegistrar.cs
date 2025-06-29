namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void Register(IKey key, Func<IDependencyResolver, object> factory, LifeTime lifeTime);
}
