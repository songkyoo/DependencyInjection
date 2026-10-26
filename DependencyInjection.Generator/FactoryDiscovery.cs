using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.DependencyInjection.Generator.Result;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class FactoryDiscovery
{
    internal static bool IsRegistrationCandidate(SyntaxNode node)
    {
        if (node is not SimpleNameSyntax simpleNameSyntax)
        {
            return false;
        }

        if (simpleNameSyntax is not
        {
            Identifier.ValueText: "Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton",
        })
        {
            return false;
        }

        return simpleNameSyntax is not GenericNameSyntax generic || generic.TypeArgumentList.Arguments.Count is 1 or 2;
    }

    internal static FactoryRegistration? Discover(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetSymbolInfo(context.Node, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var compilation = semanticModel.Compilation;
        var extensions = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions");
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var comparer = SymbolEqualityComparer.Default;

        if (!comparer.Equals(definition.ContainingType, extensions)
            || method.TypeArguments.Length is not (1 or 2)
            || comparer.Equals(
                semanticModel.GetEnclosingSymbol(context.Node.SpanStart, cancellationToken)?.ContainingType,
                extensions
            )
        )
        {
            return null;
        }

        var name = (SimpleNameSyntax)context.Node;
        var binding = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax ? name.Parent : name;

        if (binding.Parent is InvocationExpressionSyntax invocation)
        {
            binding = invocation;
        }

        var implementationIndex = method.TypeArguments.Length - 1;
        var implementation = method.TypeArguments[implementationIndex];

        if (HasErrorDiagnostics(semanticModel, binding, cancellationToken)
            || ContainsType(implementation, TypeKind.Error)
            || (implementation is INamedTypeSymbol named && HasConstructorErrors(named, compilation, cancellationToken))
        )
        {
            return null;
        }

        var location = name is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments[implementationIndex].GetLocation()
            : name.GetLocation();

        return new FactoryRegistration(implementation, location, name.GetReference());

        #region Local Functions
        static bool HasErrorDiagnostics(
            SemanticModel semanticModel1,
            SyntaxNode syntaxNode,
            CancellationToken cancellationToken
        )
        {
            return semanticModel1
                .GetDiagnostics(syntaxNode.Span, cancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        static bool HasConstructorErrors(
            INamedTypeSymbol implementation,
            Compilation compilation,
            CancellationToken cancellationToken
        )
        {
            return implementation
                .InstanceConstructors
                .SelectMany(constructor => constructor.DeclaringSyntaxReferences)
                .Any(reference =>
                {
                    return compilation
                        .GetSemanticModel(reference.SyntaxTree)
                        .GetDiagnostics(reference.Span, cancellationToken)
                        .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                });
        }
        #endregion
    }

    internal static FactoryGenerationResult CreateDefinitions(
        ImmutableArray<FactoryRegistration> registrations,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(All));

        var uniqueTypes = new List<INamedTypeSymbol>();
        var definitions = ImmutableArray.CreateBuilder<FactoryDefinition>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var originalRegistration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = originalRegistration.NameReference.GetSyntax(cancellationToken);
            var symbol = compilation
                .GetSemanticModel(name.SyntaxTree)
                .GetSymbolInfo(name, cancellationToken)
                .Symbol;

            if (symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var implementationType = methodSymbol.TypeArguments[^1];

            if (ContainsType(implementationType, TypeKind.Error))
            {
                continue;
            }

            var registration = originalRegistration with { Implementation = implementationType };
            var implementationResult = ValidateImplementation(registration, compilation);

            if (implementationResult.IsError)
            {
                diagnostics.Add(implementationResult.Error);

                continue;
            }

            var implementation = implementationResult.Value;

            if (uniqueTypes.Any(type => compilation.ClassifyConversion(type, implementation).IsIdentity))
            {
                continue;
            }

            var definition = CreateDefinition(registration, implementation, compilation);

            if (definition.IsError)
            {
                diagnostics.Add(definition.Error);

                continue;
            }

            uniqueTypes.Add(implementation);
            definitions.Add(definition.Value);
        }

        return new FactoryGenerationResult(definitions.ToImmutable(), diagnostics.ToImmutable());
    }

    private static Result<INamedTypeSymbol, Diagnostic> ValidateImplementation(
        FactoryRegistration registration,
        Compilation compilation
    )
    {
        var implementation = registration.Implementation;
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        if (!IsClosed(implementation))
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.ImplementationMustBeClosed,
                registration,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (ContainsType(implementation, TypeKind.Dynamic))
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.DynamicTypeNotSupported,
                registration,
                additionalLocations: implementation.Locations,
                arguments: [typeName, typeName]
            ));
        }

        if (implementation is not INamedTypeSymbol { TypeKind: (TypeKind.Class or TypeKind.Struct) } named
            || named.IsAbstract
            || named.IsStatic
            || named.IsRefLikeType
        )
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.InvalidImplementationType,
                registration,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (!compilation.IsSymbolAccessibleWithin(implementation, within: compilation.Assembly))
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.ImplementationNotAccessible,
                registration,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        return Ok(named);
    }

    private static Result<FactoryDefinition, Diagnostic> CreateDefinition(
        FactoryRegistration registration,
        INamedTypeSymbol implementation,
        Compilation compilation
    )
    {
        var constructorResult = SelectConstructor(registration, implementation, compilation);

        if (constructorResult.IsError)
        {
            return Error(constructorResult.Error);
        }

        var constructor = constructorResult.Value;
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        if (!compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.ConstructorNotAccessible,
                registration,
                additionalLocations: constructor.Locations,
                arguments: [constructor.ToDisplayString(), typeName]
            ));
        }

        var arguments = new List<string>();
        var tagAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute");

        foreach (var parameter in constructor.Parameters)
        {
            var parameterType = parameter.Type;
            var parameterTypeName = parameterType.ToDisplayString(FullyQualifiedFormat);

            if (ContainsType(parameterType, TypeKind.Dynamic))
            {
                return Error(CreateDiagnostic(
                    descriptor: FactoryDiagnostics.DynamicTypeNotSupported,
                    registration,
                    additionalLocations: parameter.Locations,
                    arguments: [parameterTypeName, typeName]
                ));
            }

            if (parameter.RefKind != RefKind.None || !IsSupportedDependency(parameterType))
            {
                return Error(CreateDiagnostic(
                    descriptor: FactoryDiagnostics.UnsupportedConstructorParameter,
                    registration,
                    additionalLocations: parameter.Locations,
                    arguments: [parameter.Name, typeName, parameter.ToDisplayString()]
                ));
            }

            if (!compilation.IsSymbolAccessibleWithin(parameterType, compilation.Assembly))
            {
                return Error(CreateDiagnostic(
                    descriptor: FactoryDiagnostics.DependencyNotAccessible,
                    registration,
                    additionalLocations: parameter.Locations,
                    arguments: [parameterTypeName, parameter.Name, typeName]
                ));
            }

            var tag = parameter.GetAttributes().FirstOrDefault(attribute =>
            {
                return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, tagAttribute);
            });
            var resolveArguments = tag is { ConstructorArguments.Length: 1 }
                ? $"tag: {TagValueRenderer.Render(tag.ConstructorArguments[0])}"
                : "";

            arguments.Add($"resolver.Resolve<{parameterTypeName}>({resolveArguments})");
        }

        var creationExpression = $"new {typeName}({string.Join(", ", arguments)})";

        return Ok(new FactoryDefinition(typeName, creationExpression));
    }

    private static Result<IMethodSymbol, Diagnostic> SelectConstructor(
        FactoryRegistration registration,
        INamedTypeSymbol implementation,
        Compilation compilation
    )
    {
        var comparer = SymbolEqualityComparer.Default;

        var injectAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectAttribute");
        var marked = implementation
            .InstanceConstructors
            .Where(constructor => injectAttribute is not null && constructor.GetAttributes().Any(attribute =>
            {
                return comparer.Equals(attribute.AttributeClass, injectAttribute);
            }))
            .ToArray();
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        if (marked.Length > 1)
        {
            var locations = marked
                .SelectMany(constructor => constructor.GetAttributes())
                .Where(attribute => comparer.Equals(attribute.AttributeClass, injectAttribute))
                .Select(attribute => attribute.ApplicationSyntaxReference)
                .Where(reference => reference is not null)
                .Select(reference => reference!.GetSyntax().GetLocation());

            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.MultipleInjectConstructors,
                registration,
                additionalLocations: locations,
                arguments: [typeName]
            ));
        }

        if (marked.Length == 1)
        {
            return Ok(marked[0]);
        }

        var publicConstructors = implementation
            .InstanceConstructors
            .Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        if (publicConstructors.Length == 0)
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.NoPublicConstructor,
                registration,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (publicConstructors.Length > 1)
        {
            return Error(CreateDiagnostic(
                descriptor: FactoryDiagnostics.AmbiguousPublicConstructors,
                registration,
                additionalLocations: publicConstructors.SelectMany(constructor => constructor.Locations),
                arguments: [typeName]
            ));
        }

        return Ok(publicConstructors[0]);
    }

    private static bool ContainsType(ITypeSymbol type, TypeKind kind)
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

    private static bool IsClosed(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.TypeParameter)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsClosed(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return true;
        }

        if (named.IsUnboundGenericType)
        {
            return false;
        }

        if (named.ContainingType is not null && !IsClosed(named.ContainingType))
        {
            return false;
        }

        return named.TypeArguments.All(IsClosed);
    }

    private static bool IsSupportedDependency(ITypeSymbol type)
    {
        if (type.TypeKind
            is TypeKind.TypeParameter
            or TypeKind.Error
            or TypeKind.Pointer
            or TypeKind.FunctionPointer
            || type.IsRefLikeType
        )
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedDependency(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return true;
        }

        if (named.IsUnboundGenericType)
        {
            return false;
        }

        if (named.ContainingType is not null && !IsSupportedDependency(named.ContainingType))
        {
            return false;
        }

        return named.TypeArguments.All(IsSupportedDependency);
    }

    private static Diagnostic CreateDiagnostic(
        DiagnosticDescriptor descriptor,
        FactoryRegistration registration,
        IEnumerable<Location> additionalLocations,
        params object[] arguments
    )
    {
        return Diagnostic.Create(
            descriptor,
            location: registration.Location,
            additionalLocations.Where(location => location.IsInSource),
            properties: null,
            messageArgs: arguments
        );
    }
}
