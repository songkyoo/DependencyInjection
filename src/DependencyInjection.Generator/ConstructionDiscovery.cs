using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Generator.HintNameHelpers;
using static Macaron.DependencyInjection.Generator.KeywordNameHelpers;
using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;
using static Macaron.DependencyInjection.Analysis.CompilationValidation;
using static Macaron.DependencyInjection.Generator.Result;
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

        return GenerationApiSelection.IsCandidate(simpleNameSyntax);
    }

    internal static ConstructionRequest? Discover(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;
        var name = (SimpleNameSyntax)context.Node;

        if (GenerationApiSelection.GetBoundMethod(name, semanticModel, cancellationToken) is not { } method)
        {
            return null;
        }

        var compilation = semanticModel.Compilation;
        var implementationIndex = GenerationApiSelection.GetTargetTypeArgumentIndex(method, compilation);
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var comparer = SymbolEqualityComparer.Default;

        if (implementationIndex < 0
            || comparer.Equals(
                semanticModel.GetEnclosingSymbol(context.Node.SpanStart, cancellationToken)?.ContainingType,
                definition.ContainingType
            )
        )
        {
            return null;
        }

        var binding = GenerationApiSelection.GetBinding(name);
        var implementation = method.TypeArguments[implementationIndex];

        if (HasErrorDiagnostics(semanticModel, binding, cancellationToken)
            || ContainsType(implementation, TypeKind.Error)
            || (implementation is INamedTypeSymbol named
                && HasConstructorDeclarationErrors(named, compilation, cancellationToken)
            )
        )
        {
            return null;
        }

        var location = name is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments[implementationIndex].GetLocation()
            : name.GetLocation();

        return new ConstructionRequest(implementation, location, name.GetReference(), IsForUse: method.Name == "For");
    }

    internal static FactoryGenerationResult CreateDefinitions(
        ImmutableArray<ConstructionRequest> requests,
        CSharpCompilation sourceCompilation,
        CancellationToken cancellationToken
    )
    {
        var constructors = new List<(INamedTypeSymbol Type, CreationDefinition Definition)>();
        var definitions = ImmutableArray.CreateBuilder<FactoryDefinition>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var creations = ImmutableArray.CreateBuilder<CreationDefinition>();
        var injections = ImmutableArray.CreateBuilder<InjectionDefinition>();
        var injectionTypes = new List<INamedTypeSymbol>();
        // 모든 요청을 같은 컴파일의 심볼로 비교하며 외부 타입의 비공개 Inject도 확인한다.
        var compilation = sourceCompilation.WithOptions(
            sourceCompilation.Options.WithMetadataImportOptions(MetadataImportOptions.All)
        );

        foreach (var originalRequest in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = (SimpleNameSyntax)originalRequest.NameReference.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(name.SyntaxTree);

            if (GenerationApiSelection.GetBoundMethod(name, model, cancellationToken) is not { } methodSymbol)
            {
                continue;
            }

            var implementationIndex = GenerationApiSelection.GetTargetTypeArgumentIndex(methodSymbol, compilation);

            if (implementationIndex < 0)
            {
                continue;
            }

            var implementationType = methodSymbol.TypeArguments[implementationIndex];

            if (!originalRequest.IsForUse
                && (HasAssistedConstructor(implementationType, compilation)
                    || InjectionDeclarationAnalysis.HasMethodInjection(implementationType, compilation)
                )
            )
            {
                continue;
            }

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
            var declaration = InjectionDeclarationAnalysis.Analyze(
                implementation,
                compilation,
                useLocation: request.Location,
                cancellationToken
            );

            // 독립 선언 오류는 Analyzer가 보고하며 여기서는 잘못된 출력만 막는다.
            if (declaration.HasErrors)
            {
                continue;
            }

            if (declaration.Member is { MethodKind: not MethodKind.Constructor } injectionMethod)
            {
                if (injectionTypes.Any(type =>
                {
                    return compilation.ClassifyConversion(source: type, destination: implementation).IsIdentity;
                }))
                {
                    continue;
                }

                var injection = CreateInjectionDefinition(request, implementation, injectionMethod, compilation);

                if (injection.IsError)
                {
                    diagnostics.Add(injection.Error);
                }
                else if (!injections.Contains(injection.Value))
                {
                    injections.Add(injection.Value);
                    injectionTypes.Add(implementation);
                }

                continue;
            }
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

            var definition = CreateDefinition(request, implementation, declaration.Member, compilation);

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

        return new FactoryGenerationResult(
            Definitions: definitions.ToImmutable(),
            Creations: creations.ToImmutable(),
            Injections: injections.ToImmutable(),
            Diagnostics: diagnostics.ToImmutable()
        );

        #region Local Functions
        void AddCreation(ConstructionRequest request, CreationDefinition definition)
        {
            if (request.IsForUse && !creations.Contains(definition))
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

    private static Result<InjectionDefinition, Diagnostic> CreateInjectionDefinition(
        ConstructionRequest request,
        INamedTypeSymbol implementation,
        IMethodSymbol method,
        Compilation compilation
    )
    {
        var diagnostic = InjectionDeclarationAnalysis.ValidateMethodUse(method, compilation, request.Location);

        if (diagnostic is not null)
        {
            return Error(diagnostic);
        }

        var builder = new InjectionArgumentsBuilder(method, compilation);
        var contextName = builder.ReserveName("context");
        var targetName = builder.ReserveName("instance");
        var resolverName = builder.ReserveName("resolver");
        var arguments = builder.Build(request.Location, resolverName);

        if (arguments.IsError)
        {
            return Error(arguments.Error);
        }

        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);
        var signatureFormat = AssistedParameterRenderer.SignatureTypeFormat;

        return Ok(new InjectionDefinition(
            HintName: GetCreationHintName(implementation.Name, implementation.Arity, typeName),
            ContextTypeName: implementation.ToDisplayString(signatureFormat),
            TargetTypeName: implementation.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(signatureFormat),
            IsValueType: implementation.IsValueType,
            MethodName: EscapeName(method.Name),
            Parameters: arguments.Value.AssistedParameters,
            Arguments: arguments.Value.Arguments,
            DependencyStatements: arguments.Value.DependencyStatements,
            ContextName: contextName,
            TargetName: targetName,
            ResolverName: resolverName
        ));
    }

    private static Result<CreationDefinition, Diagnostic> CreateDefinition(
        ConstructionRequest request,
        INamedTypeSymbol implementation,
        IMethodSymbol? markedConstructor,
        Compilation compilation
    )
    {
        var constructorResult = markedConstructor is not null
            ? Ok<IMethodSymbol, Diagnostic>(markedConstructor)
            : SelectConstructor(request, implementation, compilation);

        if (constructorResult.IsError)
        {
            return Error(constructorResult.Error);
        }

        var constructor = constructorResult.Value;
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);

        var argumentBuilder = new InjectionArgumentsBuilder(constructor, compilation);
        var contextName = argumentBuilder.ReserveName("context");
        var resolverName = argumentBuilder.ReserveName("resolver");
        var argumentsResult = argumentBuilder.Build(useLocation: request.Location, resolverName);

        if (argumentsResult.IsError)
        {
            return Error(argumentsResult.Error);
        }

        var preparedArguments = argumentsResult.Value;
        var signatureType = preparedArguments.AssistedParameters.Length > 0
            ? implementation
            : implementation.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var signatureTypeName = signatureType.ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);
        var constructedTypeName = implementation
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);

        var creationExpression = $"new {constructedTypeName}({preparedArguments.Arguments})";
        var factory = new FactoryDefinition(
            TypeName: signatureTypeName,
            CreationExpression: creationExpression,
            DependencyStatements: preparedArguments.DependencyStatements
        );
        var hintName = GetCreationHintName(implementation.Name, implementation.Arity, typeName);

        return Ok(new CreationDefinition(
            HintName: hintName,
            Factory: factory,
            Parameters: preparedArguments.AssistedParameters,
            ContextName: contextName,
            ResolverName: resolverName
        ));
    }

    private static Result<IMethodSymbol, Diagnostic> SelectConstructor(
        ConstructionRequest request,
        INamedTypeSymbol implementation,
        Compilation compilation
    )
    {
        var typeName = implementation.ToDisplayString(FullyQualifiedFormat);
        var constructors = implementation
            .InstanceConstructors
            .Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, within: compilation.Assembly))
            .ToArray();

        return constructors.Length switch
        {
            0 => Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.NoAccessibleConstructor,
                request,
                additionalLocations: implementation.Locations,
                arguments: [typeName]
            )),
            > 1 => Error(CreateDiagnostic(
                descriptor: DiagnosticDescriptors.AmbiguousConstructors,
                request,
                additionalLocations: constructors.SelectMany(constructor => constructor.Locations),
                arguments: [typeName]
            )),
            _ => Ok(constructors[0]),
        };
    }

    internal static bool IsClosed(ITypeSymbol type)
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
