namespace Macaron.DependencyInjection;

public delegate T DependencyFactory<out T>(
    IDependencyContainer container,
    ref CleanupCollector cleanupCollector
) where T : notnull;
