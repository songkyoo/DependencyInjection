using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Generator;

internal sealed record ConstructionRequest(
    ITypeSymbol Implementation,
    Location Location,
    SyntaxReference NameReference,
    bool GenerateCreate
);
