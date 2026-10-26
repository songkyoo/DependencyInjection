namespace Macaron.DependencyInjection.Generator;

internal sealed record FactoryDefinition(
    string TypeName,
    string CreationExpression,
    string DependencyStatements
);
