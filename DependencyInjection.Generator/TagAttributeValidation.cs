using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

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

        var tag = parameter.GetAttributes().FirstOrDefault(attribute =>
        {
            return attribute.ApplicationSyntaxReference?.Span == syntax.Span;
        });

        if (HasArrayValue(tag))
        {
            diagnostics.Add(Diagnostic.Create(
                descriptor: DiagnosticDescriptors.ArrayTagNotSupported,
                location: syntax.ArgumentList!.Arguments[0].Expression.GetLocation(),
                messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
            ));
        }

        var valueDiagnostic = ValidateValueAccessibility(
            tag, parameter, semanticModel.Compilation,
            location: syntax.ArgumentList?.Arguments.FirstOrDefault()?.Expression.GetLocation() ?? syntax.GetLocation()
        );

        if (valueDiagnostic is not null)
        {
            diagnostics.Add(valueDiagnostic);
        }

        var injectDiagnostic = InjectionParameterValidation.ValidateInjectRequirement(
            parameter, semanticModel.Compilation, attributeName: "Tag", syntax.GetLocation()
        );

        if (injectDiagnostic is not null)
        {
            diagnostics.Add(injectDiagnostic);
        }

        var shapeDiagnostic = InjectionParameterValidation.ValidateShape(
            parameter,
            attributeName: "Tag",
            location: parameterSyntax.Identifier.GetLocation()
        );

        if (shapeDiagnostic is not null)
        {
            diagnostics.Add(shapeDiagnostic);
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
            foreach (var parameter in constructor.Parameters)
            {
                var tag = parameter.GetAttributes().FirstOrDefault(attribute =>
                {
                    return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, tagAttribute);
                });
                if (tag is null)
                {
                    continue;
                }

                if (HasArrayValue(tag))
                {
                    return Diagnostic.Create(
                        descriptor: DiagnosticDescriptors.ArrayTagNotSupported,
                        location: registrationLocation,
                        messageArgs: [parameter.Name, constructor.ToDisplayString()]
                    );
                }

                var diagnostic = ValidateValueAccessibility(
                    tag, parameter, compilation, location: registrationLocation
                ) ?? InjectionParameterValidation.ValidateInjectRequirement(
                    parameter,
                    compilation,
                    attributeName: "Tag",
                    location: registrationLocation
                ) ?? InjectionParameterValidation.ValidateShape(
                    parameter,
                    attributeName: "Tag",
                    location: registrationLocation,
                    additionalLocations: parameter.Locations
                );

                if (diagnostic is not null)
                {
                    return diagnostic;
                }
            }
        }

        return null;
    }

    private static Diagnostic? ValidateValueAccessibility(
        AttributeData? tag,
        IParameterSymbol parameter,
        Compilation compilation,
        Location location
    )
    {
        if (tag is not { ConstructorArguments: [{ IsNull: false } value] })
        {
            return null;
        }

        var valueType = value.Kind switch
        {
            TypedConstantKind.Enum => value.Type,
            TypedConstantKind.Type => value.Value as ITypeSymbol,
            _ => null,
        };

        if (valueType is null || compilation.IsSymbolAccessibleWithin(valueType, compilation.Assembly))
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.TagValueTypeNotAccessible,
            location,
            messageArgs: [valueType.ToDisplayString(FullyQualifiedFormat), parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
        );
    }

    private static bool HasArrayValue(AttributeData? attribute)
    {
        return attribute is { ConstructorArguments: [{ Kind: TypedConstantKind.Array }] };
    }
}
