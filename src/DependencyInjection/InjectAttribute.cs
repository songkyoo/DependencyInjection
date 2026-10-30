namespace Macaron.DependencyInjection;

[AttributeUsage(validOn: AttributeTargets.Constructor | AttributeTargets.Method)]
public sealed class InjectAttribute : Attribute
{
}
