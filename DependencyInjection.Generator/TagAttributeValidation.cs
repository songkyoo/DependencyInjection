using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.DependencyInjection.Generator;

internal static class TagAttributeValidation
{
    internal static Diagnostic? ValidateDeclaration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var syntax = (AttributeSyntax)context.Node;
        var parameterSyntax = (ParameterSyntax)syntax.Parent!.Parent!;
        var semanticModel = context.SemanticModel;
        var tagAttribute = semanticModel.Compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute");

        if (semanticModel.GetSymbolInfo(syntax, cancellationToken).Symbol is not IMethodSymbol constructor
            || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, tagAttribute)
            || semanticModel.GetDiagnostics(parameterSyntax.Span, cancellationToken).Any(diagnostic =>
            {
                return diagnostic.Severity == DiagnosticSeverity.Error;
            })
            || semanticModel.GetDeclaredSymbol(parameterSyntax, cancellationToken) is not IParameterSymbol parameter
        )
        {
            return null;
        }

        if (!HasArrayValue(parameter.GetAttributes().FirstOrDefault(attribute =>
        {
            return attribute.ApplicationSyntaxReference?.Span == syntax.Span;
        })))
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: FactoryDiagnostics.ArrayTagNotSupported,
            location: syntax.ArgumentList!.Arguments[0].Expression.GetLocation(),
            messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
        );
    }

    internal static bool HasInvalidSourceConstructor(INamedTypeSymbol implementation, Compilation compilation)
    {
        if (!implementation.Locations.Any(location => location.IsInSource))
        {
            return false;
        }

        var tagAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute");

        return implementation
            .InstanceConstructors
            .SelectMany(constructor => constructor.Parameters)
            .SelectMany(parameter => parameter.GetAttributes())
            .Any(attribute =>
            {
                var comparer = SymbolEqualityComparer.Default;

                return comparer.Equals(attribute.AttributeClass, tagAttribute) && HasArrayValue(attribute);
            });
    }

    internal static bool HasArrayValue(AttributeData? attribute)
    {
        return attribute is { ConstructorArguments: [{ Kind: TypedConstantKind.Array }] };
    }
}
