using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainerBuilder : IDependencyRegistrar
{
    #region Fields
    private readonly ImmutableDictionary<(Type, string), TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<(Type, string), TypeRegistration>();
    #endregion

    #region IDependencyRegistrar Inteface
    void IDependencyRegistrar.RegisterTransient(Type type, string tag, Func<IDependencyContainer, object> factory)
    {
        var key = (type, tag);
        _typeRegistrations.Add(key, new TypeRegistration.Transient(factory));
    }

    void IDependencyRegistrar.RegisterScoped(Type type, string tag, Func<IDependencyContainer, object> factory)
    {
        var key = (type, tag);
        _typeRegistrations.Add(key, new TypeRegistration.Scoped(factory));
    }

    void IDependencyRegistrar.RegisterSingleton(
        Type type,
        string tag,
        Func<IDependencyContainer, object> factory,
        Ownership ownership
    )
    {
        var key = (type, tag);
        _typeRegistrations.Add(key, new TypeRegistration.Singleton(factory, ExternallyOwned: ownership switch
        {
            Ownership.Container => false,
            Ownership.External => true,
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(ownership)),
        }));
    }
    #endregion

    #region Methods
    public DependencyContainer Build(DependencyContainer? parent = null)
    {
        return new DependencyContainer(parent, registry: _typeRegistrations.ToImmutable());
    }
    #endregion
}
