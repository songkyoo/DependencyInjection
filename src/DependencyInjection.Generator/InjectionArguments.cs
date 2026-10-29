namespace Macaron.DependencyInjection.Generator;

internal sealed record InjectionArguments(
    string Arguments,
    string DependencyStatements,
    string AssistedParameters
);
