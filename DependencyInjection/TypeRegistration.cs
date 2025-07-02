namespace Macaron.DependencyInjection;

public sealed record TypeRegistration(
    LifeTime LifeTime,
    Func<IDependencyContainer, object> Factory
);
