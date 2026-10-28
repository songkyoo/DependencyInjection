namespace Macaron.DependencyInjection.Generator;

internal sealed record CreationDefinition(
    string HintName,
    FactoryDefinition Factory,
    string Parameters,
    string ContextName,
    string ResolverName
);
