namespace Macaron.DependencyInjection;

internal abstract record TypeRegistration
{
    internal sealed record Transient(RegistrationSource Source, Action<object>? Cleanup) : TypeRegistration;

    internal sealed record Scoped(RegistrationSource Source, Action<object>? Cleanup) : TypeRegistration;

    internal sealed record Singleton(RegistrationSource Source, Action<object>? Cleanup) : TypeRegistration;
}
