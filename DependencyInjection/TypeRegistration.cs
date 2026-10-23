namespace Macaron.DependencyInjection;

internal abstract record TypeRegistration
{
    public sealed record Transient(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;

    public sealed record Scoped(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;

    public sealed record Singleton(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;
}
