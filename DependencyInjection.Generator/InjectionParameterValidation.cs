using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class InjectionParameterValidation
{
    internal static ImmutableArray<Diagnostic> ValidateDeclaration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var syntax = (AttributeSyntax)context.Node;
        var parameterSyntax = (ParameterSyntax)syntax.Parent!.Parent!;
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;

        if (semanticModel.GetSymbolInfo(syntax, cancellationToken).Symbol is not IMethodSymbol attributeConstructor
            || semanticModel.GetDiagnostics(parameterSyntax.Span, cancellationToken).Any(diagnostic =>
            {
                return diagnostic.Severity == DiagnosticSeverity.Error;
            })
            || semanticModel.GetDeclaredSymbol(parameterSyntax, cancellationToken) is not IParameterSymbol parameter
        )
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var comparer = SymbolEqualityComparer.Default;
        var attributeClass = attributeConstructor.ContainingType;
        var isTag = comparer.Equals(
            attributeClass,
            compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute")
        );
        var isAssisted = comparer.Equals(
            attributeClass,
            compilation.GetTypeByMetadataName("Macaron.DependencyInjection.AssistedAttribute")
        );

        if (!isTag && !isAssisted)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var attributeName = isTag ? "Tag" : "Assisted";

        if (isTag)
        {
            var tag = FindAttribute(parameter, compilation, metadataName: "TagAttribute");
            var valueLocation = syntax.ArgumentList?.Arguments.FirstOrDefault()?.Expression.GetLocation() ?? syntax.GetLocation();

            Add(TagAttributeValidation.ValidateParameter(tag, parameter, compilation, valueLocation));
        }
        else if (FindAttribute(parameter, compilation, metadataName: "TagAttribute") is not null)
        {
            Add(CreateConflictDiagnostic(parameter, syntax.GetLocation()));
        }

        Add(ValidateInjectRequirement(parameter, compilation, attributeName, syntax.GetLocation()));
        Add(ValidateShape(parameter, attributeName, parameterSyntax.Identifier.GetLocation()));

        return diagnostics.ToImmutable();

        #region Local Functions
        void Add(Diagnostic? diagnostic)
        {
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
            }
        }
        #endregion
    }

    internal static AttributeData? FindAttribute(
        IParameterSymbol parameter,
        Compilation compilation,
        string metadataName
    )
    {
        var attributeType = compilation.GetTypeByMetadataName($"Macaron.DependencyInjection.{metadataName}");

        return parameter.GetAttributes().FirstOrDefault(attribute =>
        {
            return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType);
        });
    }

    internal static Diagnostic? ValidateConstructors(
        INamedTypeSymbol implementation,
        Compilation compilation,
        Location useLocation
    )
    {
        foreach (var constructor in implementation.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var tag = FindAttribute(parameter, compilation, metadataName: "TagAttribute");
                var assisted = FindAttribute(parameter, compilation, metadataName: "AssistedAttribute");

                if (tag is null && assisted is null)
                {
                    continue;
                }

                if (tag is not null && assisted is not null)
                {
                    return CreateConflictDiagnostic(parameter, useLocation);
                }

                var diagnostic = TagAttributeValidation.ValidateParameter(tag, parameter, compilation, useLocation)
                    ?? ValidateInjectRequirement(parameter, compilation, tag is null ? "Assisted" : "Tag", useLocation)
                    ?? ValidateShape(parameter, tag is null ? "Assisted" : "Tag", useLocation, parameter.Locations);

                if (diagnostic is not null)
                {
                    return diagnostic;
                }
            }
        }

        return null;
    }

    internal static Diagnostic? ValidateShape(
        IParameterSymbol parameter,
        string attributeName,
        Location location,
        IEnumerable<Location>? additionalLocations = null
    )
    {
        var type = parameter.Type;
        var memberName = parameter.ContainingSymbol.ToDisplayString();

        if (ContainsType(type, TypeKind.Dynamic))
        {
            return Diagnostic.Create(
                descriptor: DiagnosticDescriptors.DynamicTypeNotSupported,
                location,
                additionalLocations: additionalLocations?.Where(location => location.IsInSource),
                properties: null,
                messageArgs: [type.ToDisplayString(FullyQualifiedFormat), memberName, $"{attributeName} parameter '{parameter.Name}'"]
            );
        }

        if (parameter.RefKind is not (RefKind.None or RefKind.In) || !IsSupportedType(type))
        {
            return Diagnostic.Create(
                descriptor: DiagnosticDescriptors.UnsupportedParameter,
                location,
                additionalLocations: additionalLocations?.Where(location => location.IsInSource),
                properties: null,
                messageArgs: [parameter.Name, memberName, parameter.ToDisplayString(), attributeName]
            );
        }

        return null;
    }

    internal static bool ContainsType(ITypeSymbol type, TypeKind kind)
    {
        if (type.TypeKind == kind)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsType(array.ElementType, kind);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.ContainingType is not null && ContainsType(named.ContainingType, kind))
        {
            return true;
        }

        return named.TypeArguments.Any(argument => ContainsType(argument, kind));
    }

    private static bool IsSupportedType(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer || type.IsRefLikeType)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedType(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return true;
        }

        if (named.IsUnboundGenericType)
        {
            return false;
        }

        if (named.ContainingType is not null && !IsSupportedType(named.ContainingType))
        {
            return false;
        }

        return named.TypeArguments.All(IsSupportedType);
    }

    private static Diagnostic CreateConflictDiagnostic(IParameterSymbol parameter, Location location)
    {
        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.ConflictingParameterAttributes,
            location,
            messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
        );
    }

    private static Diagnostic? ValidateInjectRequirement(
        IParameterSymbol parameter,
        Compilation compilation,
        string attributeName,
        Location location
    )
    {
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            return null;
        }

        var injectAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");

        if (constructor.GetAttributes().Any(attribute =>
        {
            return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttribute);
        }))
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.RequiresInject,
            location,
            messageArgs: [parameter.Name, constructor.ToDisplayString(), attributeName]
        );
    }
}
