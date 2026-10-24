namespace Macaron.DependencyInjection;

internal abstract record TypeRegistration
{
    internal sealed record Transient(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;

    internal sealed record Scoped(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;

    internal sealed record Singleton(DependencyFactory<object> Factory, Action<object>? Cleanup) : TypeRegistration;
}
