namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void Register(Type type, string tag, Func<IDependencyResolver, object> factory, LifeTime lifeTime);
}
