using System.Collections.Immutable;
using Macaron.DependencyInjection.Internal;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainerBuilder
{
    #region Fields
    private readonly ImmutableDictionary<IKey, TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<IKey, TypeRegistration>();
    #endregion

    #region Methods
    public void Register(IKey key, Func<IDependencyResolver, object> factory, LifeTime lifeTime)
    {
        _typeRegistrations.Add(key, lifeTime switch
        {
            LifeTime.Transient or LifeTime.Scoped or LifeTime.Singleton => new TypeRegistration(lifeTime, factory),
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(lifeTime)),
        });
    }

    public void Register<T>(Key<T> key, Func<IDependencyResolver, T> factory, LifeTime lifeTime)
        where T : notnull
    {
        Register(key, factory: dependencyResolver => factory.Invoke(dependencyResolver, key.Type), lifeTime);
    }

    public DependencyContainer Build(DependencyContainer? parent = null)
    {
        return new DependencyContainer(parent, registry: _typeRegistrations.ToImmutable());
    }
    #endregion
}
