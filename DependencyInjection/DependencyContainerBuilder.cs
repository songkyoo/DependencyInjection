using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainerBuilder : IDependencyRegistrar
{
    #region Fields
    private readonly ImmutableDictionary<object, TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<object, TypeRegistration>(KeyReferenceComparer.Instance);
    #endregion

    #region IDependencyRegistrar Inteface
    void IDependencyRegistrar.RegisterTransient<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            key,
            registration: new TypeRegistration.Transient(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }

    void IDependencyRegistrar.RegisterScoped<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            key,
            registration: new TypeRegistration.Scoped(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }

    void IDependencyRegistrar.RegisterSingleton<T>(
        Key<T> key,
        DependencyFactory<T> factory,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            key,
            registration: new TypeRegistration.Singleton(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }
    #endregion

    #region Methods
    private void AddRegistration(object key, TypeRegistration registration)
    {
        if (!_typeRegistrations.TryAdd(key, registration))
        {
            throw new ArgumentException($"Key already registered: {key}", nameof(key));
        }
    }

    internal DependencyContainer Build(DependencyContainer? parent = null)
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
