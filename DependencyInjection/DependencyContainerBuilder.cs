using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainerBuilder : IDependencyRegistrar
{
    #region Fields
    private readonly ImmutableDictionary<(Type, string), TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<(Type, string), TypeRegistration>();
    #endregion

    #region IDependencyRegistrar Inteface
    void IDependencyRegistrar.Register(Type type, string tag, Func<IDependencyContainer, object> factory, LifeTime lifeTime)
    {
        var key = (type, tag);
        _typeRegistrations.Add(key, lifeTime switch
        {
            LifeTime.Transient or LifeTime.Scoped or LifeTime.Singleton => new TypeRegistration(lifeTime, factory),
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(lifeTime)),
        });
    }
    #endregion

    #region Methods
    public DependencyContainer Build(DependencyContainer? parent = null)
    {
        return new DependencyContainer(parent, registry: _typeRegistrations.ToImmutable());
    }
    #endregion
}
