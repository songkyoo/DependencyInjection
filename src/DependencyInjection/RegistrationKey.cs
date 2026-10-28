namespace Macaron.DependencyInjection;

internal readonly record struct RegistrationKey(Type Type, object? Tag)
{
    internal bool IsResolver => Type == typeof(IDependencyResolver) && Tag is null;

    public override string ToString()
    {
        return $"RegistrationKey {{ Type = {Type}, Tag = {Tag ?? "null"} }}";
    }
}
