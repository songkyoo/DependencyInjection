namespace Macaron.DependencyInjection;

public static class IDependencyResolverValueTypeExtensions
{
    #region ResolveOrNull
    public static T? ResolveOrNull<T>(this IDependencyResolver dependencyResolver, object? tag = null)
        where T : struct
    {
        return dependencyResolver.Contains<T>(tag)
            ? dependencyResolver.Resolve<T>(tag)
            : null;
    }
    #endregion
}
