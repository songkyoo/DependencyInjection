using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainerBuilder : IDependencyRegistrar
{
    #region Fields
    private readonly ImmutableDictionary<(Type, object?), TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<(Type, object?), TypeRegistration>();
    #endregion

    #region IDependencyRegistrar Inteface
    void IDependencyRegistrar.RegisterTransient(
        Type type,
        object? tag,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        var key = (type, tag);

        _typeRegistrations.Add(key, new TypeRegistration.Transient(factory, cleanup));
    }

    void IDependencyRegistrar.RegisterScoped(
        Type type,
        object? tag,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        var key = (type, tag);

        _typeRegistrations.Add(key, new TypeRegistration.Scoped(factory, cleanup));
    }

    void IDependencyRegistrar.RegisterSingleton(
        Type type,
        object? tag,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        var key = (type, tag);

        _typeRegistrations.Add(key, new TypeRegistration.Singleton(factory, cleanup));
    }
    #endregion

    #region Methods
    public DependencyContainer Build(DependencyContainer? parent = null)
    {
        return new DependencyContainer(parent, registry: _typeRegistrations.ToImmutable());
    }
    #endregion
}
