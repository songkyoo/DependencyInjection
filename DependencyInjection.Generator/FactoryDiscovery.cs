using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class FactoryDiscovery
{
    internal static bool IsRegistrationCandidate(SyntaxNode node)
    {
        return node is GenericNameSyntax
        {
            TypeArgumentList.Arguments.Count: 2,
            Identifier.ValueText: "Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton",
        };
    }

    internal static INamedTypeSymbol? Discover(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var extensions = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions");
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (!SymbolEqualityComparer.Default.Equals(definition.ContainingType, extensions)
            || method.TypeArguments is not [_, INamedTypeSymbol implementation]
        )
        {
            return null;
        }

        if (!IsSupportedType(implementation)
            || implementation.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || implementation.IsAbstract
            || implementation.IsStatic
            || !compilation.IsSymbolAccessibleWithin(implementation, within: compilation.Assembly)
        )
        {
            return null;
        }

        return implementation;
    }

    internal static ImmutableArray<FactoryDefinition> CreateDefinitions(
        ImmutableArray<INamedTypeSymbol> implementations,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        var uniqueTypes = new List<INamedTypeSymbol>();
        var definitions = ImmutableArray.CreateBuilder<FactoryDefinition>();

        foreach (var implementation in implementations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (uniqueTypes.Any(type => compilation.ClassifyConversion(type, implementation).IsIdentity))
            {
                continue;
            }

            uniqueTypes.Add(implementation);

            if (CreateDefinition(implementation, compilation) is { } definition)
            {
                definitions.Add(definition);
            }
        }

        return definitions.ToImmutable();
    }

    private static FactoryDefinition? CreateDefinition(INamedTypeSymbol implementation, Compilation compilation)
    {
        var constructor = SelectConstructor(implementation, compilation);

        if (constructor is null || !compilation.IsSymbolAccessibleWithin(constructor, within: compilation.Assembly))
        {
            return null;
        }

        var arguments = new List<string>();

        foreach (var parameter in constructor.Parameters)
        {
            var parameterType = parameter.Type;

            if (parameter.RefKind != RefKind.None
                || !IsSupportedType(parameterType)
                || !compilation.IsSymbolAccessibleWithin(parameterType, compilation.Assembly)
            )
            {
                return null;
            }

            arguments.Add($"resolver.Resolve<{parameterType.ToDisplayString(FullyQualifiedFormat)}>()");
        }

        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);
        var creationExpression = $"new {typeName}({string.Join(", ", arguments)})";

        return new FactoryDefinition(typeName, creationExpression);
    }

    private static IMethodSymbol? SelectConstructor(INamedTypeSymbol implementation, Compilation compilation)
    {
        var injectAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");
        var marked = implementation
            .InstanceConstructors
            .Where(constructor => injectAttribute is not null && constructor.GetAttributes().Any(attribute =>
            {
                return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttribute);
            }))
            .ToArray();

        if (marked.Length > 1)
        {
            return null;
        }

        if (marked.Length == 1)
        {
            return marked[0];
        }

        var publicConstructors = implementation
            .InstanceConstructors
            .Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        return publicConstructors.Length == 1 ? publicConstructors[0] : null;
    }

    private static bool IsSupportedType(ITypeSymbol type)
    {
        if (type.TypeKind
            is TypeKind.TypeParameter
            or TypeKind.Error
            or TypeKind.Dynamic
            or TypeKind.Pointer
            or TypeKind.FunctionPointer
            || type.IsRefLikeType
        )
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedType(array.ElementType);
        }

        if (type is INamedTypeSymbol named)
        {
            return !named.IsUnboundGenericType
                && (named.ContainingType is null || IsSupportedType(named.ContainingType))
                && named.TypeArguments.All(IsSupportedType);
        }

        return true;
    }
}
