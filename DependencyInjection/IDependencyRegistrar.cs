namespace Macaron.DependencyInjection;

public interface IDependencyRegistrar
{
    void Register(Type type, string tag, Func<IDependencyContainer, object> factory, LifeTime lifeTime);
}
