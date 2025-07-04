namespace Macaron.DependencyInjection;

public abstract record TypeRegistration
{
    public sealed record Transient(Func<IDependencyContainer, object> Factory) : TypeRegistration;

    public sealed record Scoped(Func<IDependencyContainer, object> Factory) : TypeRegistration;

    public sealed record Singleton(Func<IDependencyContainer, object> Factory, bool ExternallyOwned) : TypeRegistration;
}
