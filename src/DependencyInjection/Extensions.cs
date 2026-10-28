namespace Macaron.DependencyInjection;

internal static class Extensions
{
    internal static object Invoke<T>(
        this DependencyFactory<T> factory,
        IDependencyResolver dependencyResolver,
        RegistrationKey key,
        ref CleanupCollector cleanupCollector
    ) where T : notnull
    {
        var instance = factory.Invoke(dependencyResolver, ref cleanupCollector);

        if (instance is null)
        {
            throw new InvalidOperationException($"Factory returned null for registration: {key}");
        }

        return instance;
    }
}
