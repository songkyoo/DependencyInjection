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
        RegistrationSource.Factory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Transient, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterTransient<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Transient, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterScoped<T>(
        RegistrationSource.Factory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Scoped, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterScoped<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Scoped, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterSingleton<T>(
        RegistrationSource.Instance<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Singleton, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterSingleton<T>(
        RegistrationSource.Factory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Singleton, tag, cleanup);
    }

    void IDependencyRegistrar.RegisterSingleton<T>(
        RegistrationSource.ContextFactory<T> source,
        object? tag,
        Action<T>? cleanup
    )
    {
        AddRegistration(source, Lifetime.Singleton, tag, cleanup);
    }
    #endregion

    #region Methods
    private void AddRegistration<T>(
        RegistrationSource source,
        Lifetime lifetime,
        object? tag,
        Action<T>? cleanup
    ) where T : notnull
    {
        Action<object>? objectCleanup = cleanup is null ? null : instance => cleanup((T)instance);
        TypeRegistration registration = lifetime switch
        {
            Lifetime.Transient => new TypeRegistration.Transient(source, objectCleanup),
            Lifetime.Scoped => new TypeRegistration.Scoped(source, objectCleanup),
            Lifetime.Singleton => new TypeRegistration.Singleton(source, objectCleanup),
            _ => throw new InvalidOperationException($"Not supported life time: {lifetime}")
        };

        AddRegistration(new RegistrationKey(typeof(T), tag), registration);
    }

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
