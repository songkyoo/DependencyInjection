using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainerBuilder : IDependencyRegistrar
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
        var container = new DependencyContainer(parent, registry: _typeRegistrations.ToImmutable());

        try
        {
            container.InitializeSharedInstances();

            return container;
        }
        catch (Exception creationError)
        {
            try
            {
                container.Dispose();
            }
            catch (Exception disposalError)
            {
                throw new AggregateException(creationError, disposalError);
            }

            throw new AggregateException(creationError);
        }
    }
    #endregion
}
