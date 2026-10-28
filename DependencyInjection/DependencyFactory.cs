namespace Macaron.DependencyInjection;

// Add가 변경한 항목 수를 호출자에게 반영해야 하므로 수집기는 ref로 전달한다.
public delegate T DependencyFactory<out T>(
    IDependencyResolver resolver,
    ref CleanupCollector cleanupCollector
) where T : notnull;
