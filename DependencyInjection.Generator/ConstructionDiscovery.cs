using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.DependencyInjection.Generator.InjectionParameterValidation;
using static Macaron.DependencyInjection.Generator.Result;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class ConstructionDiscovery
{
    internal static bool IsConstructionCandidate(SyntaxNode node)
    {
        if (node is not SimpleNameSyntax simpleNameSyntax)
        {
            return false;
        }

        if (simpleNameSyntax is not
        {
            Identifier.ValueText: "For" or "Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton",
        })
        {
            return false;
        }

        return simpleNameSyntax is not GenericNameSyntax generic || generic.TypeArgumentList.Arguments.Count is 1 or 2;
    }

    internal static ConstructionRequest? Discover(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetSymbolInfo(context.Node, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var compilation = semanticModel.Compilation;
        var generateCreate = method.Name == "For";
        var extensions = generateCreate
            ? compilation
                .GetTypeByMetadataName("Macaron.DependencyInjection.IDependencyResolver")?
                .ContainingAssembly
                .GetTypeByMetadataName("Macaron.DependencyInjection.IDependencyResolverExtensions")
            : compilation.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions");
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var comparer = SymbolEqualityComparer.Default;

        if (!comparer.Equals(definition.ContainingType, extensions)
            || (generateCreate
                ? method.TypeArguments.Length != 1
                : method.TypeArguments.Length is not (1 or 2)
            )
            || comparer.Equals(
                semanticModel.GetEnclosingSymbol(context.Node.SpanStart, cancellationToken)?.ContainingType,
                extensions
            )
        )
        {
            return null;
        }

        var name = (SimpleNameSyntax)context.Node;
        var binding = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
            ? name.Parent
            : name;

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

        return new ConstructionRequest(implementation, location, name.GetReference(), generateCreate);

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
        ImmutableArray<ConstructionRequest> requests,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(All));

        var uniqueTypes = new List<INamedTypeSymbol>();
        var definitions = ImmutableArray.CreateBuilder<FactoryDefinition>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var creations = ImmutableArray.CreateBuilder<CreationDefinition>();

        foreach (var originalRequest in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = originalRequest.NameReference.GetSyntax(cancellationToken);
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

            var request = originalRequest with { Implementation = implementationType };
            var implementationResult = ValidateImplementation(request, compilation);

            if (implementationResult.IsError)
            {
                diagnostics.Add(implementationResult.Error);

                continue;
            }

            var implementation = implementationResult.Value;

            var existingIndex = uniqueTypes.FindIndex(type => compilation.ClassifyConversion(type, implementation).IsIdentity);

            if (existingIndex >= 0)
            {
                AddCreation(request, implementation, definitions[existingIndex]);

                continue;
            }

            var tagDiagnostic = TagAttributeValidation.ValidateConstructors(
                implementation,
                compilation,
                registrationLocation: request.Location
            );

            if (tagDiagnostic is not null)
            {
                if (!implementation.Locations.Any(location => location.IsInSource))
                {
                    diagnostics.Add(tagDiagnostic);
                }

                continue;
            }

            var definition = CreateDefinition(request, implementation, compilation);

            if (definition.IsError)
            {
                diagnostics.Add(definition.Error);

                continue;
            }

            uniqueTypes.Add(implementation);
            definitions.Add(definition.Value);
            AddCreation(request, implementation, definition.Value);
        }

        return new FactoryGenerationResult(definitions.ToImmutable(), creations.ToImmutable(), diagnostics.ToImmutable());

        void AddCreation(ConstructionRequest request, INamedTypeSymbol implementation, FactoryDefinition factory)
        {
            if (!request.GenerateCreate || creations.Any(creation => creation.Factory == factory))
            {
                return;
            }

            var hintName = HintNameHelper.GetCreationHintName(implementation.Name, implementation.Arity, factory.TypeName);

            creations.Add(new CreationDefinition(hintName, factory));
        }
    }

    private static Result<INamedTypeSymbol, Diagnostic> ValidateImplementation(
        ConstructionRequest request,
        Compilation compilation
    )
    {
        var implementation = request.Implementation;
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        if (!IsClosed(implementation))
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.ImplementationMustBeClosed,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (ContainsType(implementation, TypeKind.Dynamic))
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.DynamicTypeNotSupported,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName, typeName, "automatic construction"]
            ));
        }

        if (implementation is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
            || named.IsAbstract
            || named.IsStatic
            || named.IsRefLikeType
        )
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.InvalidImplementationType,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (!compilation.IsSymbolAccessibleWithin(implementation, within: compilation.Assembly))
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.ImplementationNotAccessible,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        return Ok(named);
    }

    private static Result<FactoryDefinition, Diagnostic> CreateDefinition(
        ConstructionRequest request,
        INamedTypeSymbol implementation,
        Compilation compilation
    )
    {
        var constructorResult = SelectConstructor(request, implementation, compilation);

        if (constructorResult.IsError)
        {
            return Error(constructorResult.Error);
        }

        var constructor = constructorResult.Value;
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        if (!compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.ConstructorNotAccessible,
                request,
                additionalLocations: constructor.Locations,
                arguments: [constructor.ToDisplayString(), typeName]
            ));
        }

        var arguments = new List<string>();
        var dependencyStatements = new List<string>();
        var usesInParameters = constructor.Parameters.Any(parameter => parameter.RefKind == RefKind.In);
        var tagAttribute = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.TagAttribute");

        foreach (var parameter in constructor.Parameters)
        {
            var parameterType = parameter.Type;
            var parameterTypeName = parameterType.ToDisplayString(FullyQualifiedFormat);

            var tag = parameter.GetAttributes().FirstOrDefault(attribute =>
            {
                return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, tagAttribute);
            });
            var parameterDiagnostic = ValidateShape(
                parameter,
                attributeName: tag is null ? "dependency injection" : "Tag",
                request.Location,
                additionalLocations: parameter.Locations
            );

            if (parameterDiagnostic is not null)
            {
                return Error(parameterDiagnostic);
            }

            if (!compilation.IsSymbolAccessibleWithin(parameterType, compilation.Assembly))
            {
                return Error(CreateDiagnostic(
                    descriptor: DiagnosticDescriptors.DependencyNotAccessible,
                    request,
                    additionalLocations: parameter.Locations,
                    arguments: [parameterTypeName, parameter.Name, typeName]
                ));
            }

            var resolveArguments = tag is { ConstructorArguments.Length: 1 }
                ? $"tag: {TagValueRenderer.Render(tag.ConstructorArguments[0])}"
                : "";
            var resolveExpression = $"resolver.Resolve<{parameterTypeName}>({resolveArguments})";

            if (usesInParameters)
            {
                var variableName = $"dependency{arguments.Count}";

                dependencyStatements.Add($"var {variableName} = {resolveExpression};");
                arguments.Add(parameter.RefKind == RefKind.In ? $"in {variableName}" : variableName);
            }
            else
            {
                arguments.Add(resolveExpression);
            }
        }

        var creationExpression = $"new {typeName}({string.Join(", ", arguments)})";

        return Ok(new FactoryDefinition(typeName, creationExpression, string.Join("\n", dependencyStatements)));
    }

    private static Result<IMethodSymbol, Diagnostic> SelectConstructor(
        ConstructionRequest request,
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
                descriptor: DiagnosticDescriptors.MultipleInjectConstructors,
                request,
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
                descriptor: DiagnosticDescriptors.NoPublicConstructor,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            ));
        }

        if (publicConstructors.Length > 1)
        {
            return Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.AmbiguousPublicConstructors,
                request,
                additionalLocations: publicConstructors.SelectMany(constructor => constructor.Locations),
                arguments: [typeName]
            ));
        }

        return Ok(publicConstructors[0]);
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

    private static Diagnostic CreateDiagnostic(
        DiagnosticDescriptor descriptor,
        ConstructionRequest request,
        IEnumerable<Location> additionalLocations,
        params object[] arguments
    )
    {
        return Diagnostic.Create(
            descriptor,
            location: request.Location,
            additionalLocations.Where(location => location.IsInSource),
            properties: null,
            messageArgs: arguments
        );
    }
}
