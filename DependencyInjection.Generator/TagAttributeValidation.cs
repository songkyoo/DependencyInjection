using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.DependencyInjection.Generator;

internal static class TagAttributeValidation
{
    internal static ImmutableArray<Diagnostic> ValidateDeclaration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (HasArrayValue(parameter.GetAttributes().FirstOrDefault(attribute =>
        {
            return attribute.ApplicationSyntaxReference?.Span == syntax.Span;
        })))
        {
            diagnostics.Add(Diagnostic.Create(
                descriptor: FactoryDiagnostics.ArrayTagNotSupported,
                location: syntax.ArgumentList!.Arguments[0].Expression.GetLocation(),
                messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
            ));
        }

        if (IsConstructorWithoutInject(parameter.ContainingSymbol, semanticModel.Compilation))
        {
            diagnostics.Add(Diagnostic.Create(
                descriptor: FactoryDiagnostics.TaggedConstructorRequiresInject,
                location: syntax.GetLocation(),
                messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
            ));
        }

        return diagnostics.ToImmutable();
    }

    internal static Diagnostic? ValidateConstructors(
        INamedTypeSymbol implementation,
        Compilation compilation,
        Location registrationLocation
    )
    {
        var tagAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute");

        foreach (var constructor in implementation.InstanceConstructors)
        {
            var requiresInject = IsConstructorWithoutInject(constructor, compilation);

            foreach (var parameter in constructor.Parameters)
            {
                var tag = parameter.GetAttributes().FirstOrDefault(attribute =>
                {
                    return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, tagAttribute);
                });
                var descriptor = tag switch
                {
                    not null when HasArrayValue(tag) => FactoryDiagnostics.ArrayTagNotSupported,
                    not null when requiresInject => FactoryDiagnostics.TaggedConstructorRequiresInject,
                    _ => null,
                };

                if (descriptor is not null)
                {
                    return Diagnostic.Create(
                        descriptor,
                        location: registrationLocation,
                        messageArgs: [parameter.Name, constructor.ToDisplayString()]
                    );
                }
            }
        }

        return null;
    }

    private static bool IsConstructorWithoutInject(ISymbol member, Compilation compilation)
    {
        if (member is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            return false;
        }

        var injectAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");

        return !constructor.GetAttributes().Any(attribute =>
        {
            return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttribute);
        });
    }

    private static bool HasArrayValue(AttributeData? attribute)
    {
        return attribute is { ConstructorArguments: [{ Kind: TypedConstantKind.Array }] };
    }
}
