namespace Macaron.DependencyInjection;

[AttributeUsage(validOn: AttributeTargets.Parameter)]
public sealed class TagAttribute(object? value = null) : Attribute
{
    public object? Value { get; } = value;
}
