namespace Macaron.DependencyInjection;

public abstract record TypeRegistration
{
    public sealed record Transient(Func<IDependencyContainer, object> Factory, Action<object>? Cleanup) : TypeRegistration;

    public sealed record Scoped(Func<IDependencyContainer, object> Factory, Action<object>? Cleanup) : TypeRegistration;

    public sealed record Singleton(Func<IDependencyContainer, object> Factory, Action<object>? Cleanup) : TypeRegistration;
}
