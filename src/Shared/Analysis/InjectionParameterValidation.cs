using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.MethodKind;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Analysis;

internal static class InjectionParameterValidation
{
    internal static ImmutableArray<Diagnostic> ValidateDeclaration(
        AttributeSyntax syntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var parameterSyntax = (ParameterSyntax)syntax.Parent!.Parent!;
        var compilation = semanticModel.Compilation;

        if (semanticModel.GetSymbolInfo(syntax, cancellationToken).Symbol is not IMethodSymbol attributeConstructor
            || CompilationValidation.HasDeclarationErrors(semanticModel, parameterSyntax, cancellationToken)
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

        var attributeName = isTag ? "Tag" : "Assisted";
        var injectRequirement = ValidateInjectRequirement(parameter, compilation, attributeName, syntax.GetLocation());

        if (parameter.ContainingSymbol is IMethodSymbol { MethodKind: Constructor or Ordinary } member
            && !compilation.IsSymbolAccessibleWithin(member, within: compilation.Assembly)
        )
        {
            // Inject 유무는 접근성과 무관한 선언 규칙이며 지정 멤버의 유효성과 별도로 검사한다.
            return injectRequirement is null
                ? ImmutableArray<Diagnostic>.Empty
                : ImmutableArray.Create(injectRequirement);
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (isTag)
        {
            var tag = FindAttribute(parameter, compilation, metadataName: "TagAttribute");
            var valueLocation = syntax
                .ArgumentList?
                .Arguments
                .FirstOrDefault()?
                .Expression
                .GetLocation() ?? syntax.GetLocation();

            Add(TagAttributeValidation.ValidateParameter(tag, parameter, compilation, valueLocation));
        }
        else if (FindAttribute(parameter, compilation, metadataName: "TagAttribute") is not null)
        {
            Add(CreateConflictDiagnostic(parameter, syntax.GetLocation()));
        }

        Add(injectRequirement);
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

    internal static AttributeData? FindInjectAttribute(IMethodSymbol member, Compilation compilation)
    {
        var inject = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");

        return member.GetAttributes().FirstOrDefault(attribute =>
        {
            return inject is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, inject);
        });
    }

    internal static Diagnostic? ValidateMemberParameters(
        IMethodSymbol member,
        Compilation compilation,
        Location useLocation
    )
    {
        var isMarked = FindInjectAttribute(member, compilation) is not null;
        var isAccessible = compilation.IsSymbolAccessibleWithin(member, compilation.Assembly);

        foreach (var parameter in member.Parameters)
        {
            var tag = FindAttribute(parameter, compilation, metadataName: "TagAttribute");
            var assisted = FindAttribute(parameter, compilation, metadataName: "AssistedAttribute");

            // 특성 없는 생성자 인자의 형태는 기존 생성 경로에서 사용 위치에 진단한다.
            if ((!isMarked || member.MethodKind == Constructor) && tag is null && assisted is null)
            {
                continue;
            }

            var parameterLocation = parameter.Locations.FirstOrDefault(location => location.IsInSource) ?? useLocation;
            var attributeSyntax = (tag ?? assisted)?.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            var attributeLocation = attributeSyntax?.GetLocation() ?? parameterLocation;
            var attributeName = tag is not null ? "Tag" : assisted is not null ? "Assisted" : "dependency injection";
            var injectRequirement = tag is not null || assisted is not null
                ? ValidateInjectRequirement(parameter, compilation, attributeName, attributeLocation)
                : null;

            if (!isAccessible)
            {
                if (injectRequirement is not null)
                {
                    return injectRequirement;
                }

                continue;
            }

            if (tag is not null && assisted is not null)
            {
                return CreateConflictDiagnostic(parameter, attributeLocation);
            }

            var valueLocation = attributeSyntax?
                .ArgumentList?
                .Arguments
                .FirstOrDefault()?
                .Expression
                .GetLocation() ?? attributeLocation;
            var diagnostic = TagAttributeValidation.ValidateParameter(tag, parameter, compilation, valueLocation);

            diagnostic ??= injectRequirement;
            diagnostic ??= ValidateShape(parameter, attributeName, parameterLocation, parameter.Locations);

            if (diagnostic is not null)
            {
                return diagnostic;
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
                messageArgs: [
                    type.ToDisplayString(FullyQualifiedFormat),
                    memberName,
                    $"{attributeName} parameter '{parameter.Name}'"
                ]
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
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: Constructor or Ordinary } member)
        {
            return null;
        }

        if (FindInjectAttribute(member, compilation) is not null)
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.RequiresInject,
            location,
            messageArgs:
            [
                parameter.Name,
                member.ToDisplayString(),
                attributeName,
                member.MethodKind == Constructor ? "Constructor" : "Method",
            ]
        );
    }

    internal static bool HasAssistedConstructor(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var inject = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");

        return named
            .InstanceConstructors
            .Any(constructor =>
            {
                if (!compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly)
                    || !constructor
                    .GetAttributes()
                    .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, inject))
                )
                {
                    return false;
                }

                return constructor.Parameters.Any(parameter => FindAttribute(
                    parameter,
                    compilation,
                    metadataName: "AssistedAttribute"
                ) is not null);
            });
    }
}
