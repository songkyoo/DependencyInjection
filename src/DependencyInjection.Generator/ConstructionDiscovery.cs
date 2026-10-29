using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Generator.HintNameHelpers;
using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;
using static Macaron.DependencyInjection.Analysis.CompilationValidation;
using static Macaron.DependencyInjection.Generator.KeywordNameHelpers;
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
            || (implementation is INamedTypeSymbol named && HasConstructorErrors(named, compilation, cancellationToken)
        )
        )
        {
            return null;
        }

        var location = name is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments[implementationIndex].GetLocation()
            : name.GetLocation();

        return new ConstructionRequest(implementation, location, name.GetReference(), generateCreate);
    }

    internal static FactoryGenerationResult CreateDefinitions(
        ImmutableArray<ConstructionRequest> requests,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        // 외부 DLL의 비공개 생성자도 읽어야 접근 불가와 생성자 부재를 구분할 수 있다.
        compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(All));

        var constructors = new List<(INamedTypeSymbol Type, CreationDefinition Definition)>();
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

            if (!originalRequest.GenerateCreate && methodSymbol.TypeArguments.Any(type =>
            {
                return HasAssistedConstructor(type, compilation);
            }))
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
            // nullable 참조 표기와 튜플 요소 이름만 다른 타입은 같은 생성자 정의를 공유한다.
            var existingIndex = constructors.FindIndex(entry =>
            {
                return compilation.ClassifyConversion(source: entry.Type, destination: implementation).IsIdentity;
            });

            if (existingIndex >= 0)
            {
                AddCreation(request, constructors[existingIndex].Definition);

                continue;
            }

            var declarationDiagnostic = ValidateConstructors(
                implementation,
                compilation,
                useLocation: request.Location
            );

            if (declarationDiagnostic is not null)
            {
                // 독립적인 선언 진단은 Analyzer가 보고하며 생성기는 잘못된 출력만 막는다.
                continue;
            }

            var definition = CreateDefinition(request, implementation, compilation);

            if (definition.IsError)
            {
                diagnostics.Add(definition.Error);

                continue;
            }

            constructors.Add((implementation, definition.Value));

            // Assisted 인자가 필요한 생성자는 인자 없는 등록용 델리게이트로 호출할 수 없다.
            if (definition.Value.Parameters.Length == 0)
            {
                definitions.Add(definition.Value.Factory);
            }

            AddCreation(request, definition.Value);
        }

        return new FactoryGenerationResult(definitions.ToImmutable(), creations.ToImmutable(), diagnostics.ToImmutable());

        #region Local Functions
        void AddCreation(ConstructionRequest request, CreationDefinition definition)
        {
            if (request.GenerateCreate && !creations.Contains(definition))
            {
                creations.Add(definition);
            }
        }
        #endregion
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

    private static Result<CreationDefinition, Diagnostic> CreateDefinition(
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
        var assistedParameters = new List<string>();
        var reservedNames = new HashSet<string>(constructor
            .Parameters
            .Where(parameter => FindAttribute(parameter, compilation, metadataName: "AssistedAttribute") is not null)
            .Select(parameter => parameter.Name)
        );
        var contextName = GetUniqueName("context");
        var resolverName = GetUniqueName("resolver");
        var usesInParameters = constructor.Parameters.Any(parameter => parameter.RefKind == RefKind.In);

        foreach (var parameter in constructor.Parameters)
        {
            var parameterType = parameter.Type;
            var parameterTypeName = parameterType.ToDisplayString(FullyQualifiedFormat);

            var tag = FindAttribute(parameter, compilation, metadataName: "TagAttribute");
            var assisted = FindAttribute(parameter, compilation, metadataName: "AssistedAttribute");
            var attributeName = assisted is not null
                ? "Assisted"
                : tag is not null ? "Tag" : "dependency injection";
            var parameterDiagnostic = ValidateShape(
                parameter,
                attributeName,
                location: request.Location,
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

            if (assisted is not null)
            {
                assistedParameters.Add(AssistedParameterRenderer.Render(parameter, compilation));

                var name = EscapeName(parameter.Name);

                arguments.Add(parameter.RefKind == RefKind.In ? $"in {name}" : name);

                continue;
            }

            var resolveArguments = tag is { ConstructorArguments.Length: 1 }
                ? $"tag: {TagValueRenderer.Render(tag.ConstructorArguments[0], compilation)}"
                : "";
            var resolveExpression = DependencyArgumentRenderer.Render(
                parameter,
                resolverName,
                tagArguments: resolveArguments,
                compilation
            );

            if (usesInParameters)
            {
                var localTypeName = parameterType.ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);
                var variableName = GetUniqueName($"dependency{arguments.Count}");

                dependencyStatements.Add($"{localTypeName} {variableName} = {resolveExpression};");
                arguments.Add(parameter.RefKind == RefKind.In ? $"in {variableName}" : variableName);
            }
            else
            {
                arguments.Add(resolveExpression);
            }
        }

        var signatureType = assistedParameters.Count > 0
            ? implementation
            : implementation.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var signatureTypeName = signatureType.ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);
        var constructedTypeName = implementation
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);

        var creationExpression = $"new {constructedTypeName}({string.Join(", ", arguments)})";
        var factory = new FactoryDefinition(
            TypeName: signatureTypeName,
            CreationExpression: creationExpression,
            DependencyStatements: string.Join("\n", dependencyStatements)
        );
        var hintName = GetCreationHintName(implementation.Name, implementation.Arity, typeName);

        return Ok(new CreationDefinition(
            HintName: hintName,
            Factory: factory,
            Parameters: string.Join(", ", assistedParameters),
            ContextName: contextName,
            ResolverName: resolverName
        ));

        #region Local Functions
        string GetUniqueName(string name)
        {
            var candidate = name;
            var suffix = 1;

            while (!reservedNames.Add(candidate))
            {
                candidate = name + suffix++;
            }

            return candidate;
        }
        #endregion
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
