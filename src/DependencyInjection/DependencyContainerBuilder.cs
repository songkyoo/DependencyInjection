using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainerBuilder : IDependencyRegistrar
{
    #region Fields
    private readonly ImmutableDictionary<RegistrationKey, TypeRegistration>.Builder _typeRegistrations =
        ImmutableDictionary.CreateBuilder<RegistrationKey, TypeRegistration>();
    #endregion

    #region IDependencyRegistrar Inteface
    void IDependencyRegistrar.RegisterTransient<T>(
        DependencyFactory<T> factory,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            new RegistrationKey(typeof(T), tag),
            registration: new TypeRegistration.Transient(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }

    void IDependencyRegistrar.RegisterScoped<T>(
        DependencyFactory<T> factory,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            new RegistrationKey(typeof(T), tag),
            registration: new TypeRegistration.Scoped(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }

    void IDependencyRegistrar.RegisterSingleton<T>(
        DependencyFactory<T> factory,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(
            new RegistrationKey(typeof(T), tag),
            registration: new TypeRegistration.Singleton(
                Factory: (resolver, ref collector) => factory.Invoke(resolver, ref collector),
                Cleanup: cleanup is null ? null : instance => cleanup((T)instance)
            )
        );
    }
    #endregion

    #region Methods
    private void AddRegistration(RegistrationKey key, TypeRegistration registration)
    {
        if (Nullable.GetUnderlyingType(key.Type) is not null)
        {
            throw new ArgumentException($"Nullable value types cannot be registered as service types: {key.Type}");
        }

        if (key.IsResolver)
        {
            throw new ArgumentException($"Key is reserved for the current dependency resolver: {key}", paramName: nameof(key));
        }

        if (!_typeRegistrations.TryAdd(key, registration))
        {
            throw new ArgumentException($"Key already registered: {key}", paramName: nameof(key));
        }
    }

    internal DependencyContainer Build(DependencyContainer? parent = null)
    {
        return DependencyContainer.Create(parent, registry: _typeRegistrations.ToImmutable());
    }
    #endregion
}
