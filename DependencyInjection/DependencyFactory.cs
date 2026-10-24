namespace Macaron.DependencyInjection;

public delegate T DependencyFactory<out T>(
    IDependencyResolver resolver,
    ref CleanupCollector cleanupCollector
) where T : notnull;
