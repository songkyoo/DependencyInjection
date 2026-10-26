using Microsoft.CodeAnalysis;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class InjectionParameterValidation
{
    internal static Diagnostic? ValidateInjectRequirement(
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
            DiagnosticDescriptors.RequiresInject,
            location,
            parameter.Name, constructor.ToDisplayString(), attributeName
        );
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
                DiagnosticDescriptors.DynamicTypeNotSupported,
                location,
                additionalLocations: additionalLocations?.Where(location => location.IsInSource),
                properties: null,
                messageArgs: [type.ToDisplayString(FullyQualifiedFormat), memberName, $"{attributeName} parameter '{parameter.Name}'"]
            );
        }

        if (parameter.RefKind is not (RefKind.None or RefKind.In) || !IsSupportedType(type))
        {
            return Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedParameter,
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
}
