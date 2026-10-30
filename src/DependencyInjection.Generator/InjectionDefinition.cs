namespace Macaron.DependencyInjection.Generator;

internal sealed record InjectionDefinition(
    string HintName,
    string ContextTypeName,
    string TargetTypeName,
    bool IsValueType,
    string MethodName,
    string Parameters,
    string Arguments,
    string DependencyStatements,
    string ContextName,
    string TargetName,
    string ResolverName
);
