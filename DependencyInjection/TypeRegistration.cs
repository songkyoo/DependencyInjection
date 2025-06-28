namespace Macaron.DependencyInjection;

public sealed record TypeRegistration(
    LifeTime LifeTime,
    Func<IDependencyResolver, object> Factory
);
