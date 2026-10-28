using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.DependencyInjection.Generator;

internal static class RegistrationValidation
{
    internal static bool IsCandidate(SyntaxNode node)
    {
        return node is SimpleNameSyntax
        {
            Identifier.ValueText: "Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton",
        };
    }

    internal static ImmutableArray<Diagnostic> ValidateRegistrations(
        ImmutableArray<SyntaxReference> registrations,
        CSharpCompilation compilation,
        ImmutableArray<(string HintName, string Source)> creations,
        CancellationToken cancellationToken)
    {
        var parseOptions = compilation
            .SyntaxTrees
            .FirstOrDefault()?
            .Options as CSharpParseOptions ?? CSharpParseOptions.Default;
        // 생성된 Create 시그니처로 팩토리 람다를 바인딩하고 등록 T의 추론을 컴파일러에 맡긴다.
        var creationTrees = creations.Select(creation => CSharpSyntaxTree.ParseText(
            text: creation.Source,
            options: parseOptions,
            path: creation.HintName,
            cancellationToken: cancellationToken
        ));

        compilation = compilation
            .AddSyntaxTrees(creationTrees)
            .WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));

        var comparer = SymbolEqualityComparer.Default;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var reference in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = (SimpleNameSyntax)reference.GetSyntax(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(name.SyntaxTree);

            if (semanticModel.GetSymbolInfo(name, cancellationToken).Symbol is not IMethodSymbol method
                || !IsRegistrationApi(method, compilation)
            )
            {
                continue;
            }

            var owner = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

            // 등록 확장 내부의 위임 호출은 소비자의 등록 요청과 중복해서 검사하지 않는다.
            if (comparer.Equals(
                semanticModel.GetEnclosingSymbol(name.SpanStart, cancellationToken)?.ContainingType,
                owner
            ))
            {
                continue;
            }

            var binding = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
                ? name.Parent
                : name;

            if (binding.Parent is InvocationExpressionSyntax invocation)
            {
                binding = invocation;
            }

            if (semanticModel.GetDiagnostics(binding.Span, cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                continue;
            }

            for (var i = 0; i < method.TypeArguments.Length; i++)
            {
                var type = method.TypeArguments[i];
                var location = name is GenericNameSyntax generic
                    ? generic.TypeArgumentList.Arguments[i].GetLocation()
                    : name.GetLocation();
                var generatedExtensions = compilation.GetTypeByMetadataName(
                    "Macaron.DependencyInjection.DependencyRegistrarExtensions"
                );
                var isAutomaticRegistration = comparer.Equals(owner, generatedExtensions);

                if (!isAutomaticRegistration && type is INamedTypeSymbol named && !named.Locations.Any(source =>
                {
                    return source.IsInSource;
                }))
                {
                    var declarationDiagnostic = InjectionParameterValidation.ValidateConstructors(
                        implementation: named,
                        compilation,
                        useLocation: location
                    );

                    if (declarationDiagnostic is not null)
                    {
                        diagnostics.Add(declarationDiagnostic);
                    }
                }

                if (!HasAssistedConstructor(type, compilation)
                    || ConstructionDiscovery.HasConstructorErrors(type, compilation, cancellationToken)
                )
                {
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    descriptor: DiagnosticDescriptors.AssistedTypeCannotBeRegistered,
                    location,
                    additionalLocations: type.Locations.Where(location => location.IsInSource),
                    properties: null,
                    messageArgs: [type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]
                ));
            }
        }

        return diagnostics.ToImmutable();
    }

    private static bool IsRegistrationApi(IMethodSymbol method, Compilation compilation)
    {
        var registrar = compilation.GetTypeByMetadataName(
            "Macaron.DependencyInjection.IDependencyRegistrar"
        );
        var runtimeExtensions = registrar?.ContainingAssembly.GetTypeByMetadataName(
            "Macaron.DependencyInjection.IDependencyRegistrarExtensions"
        );
        var generatedExtensions = compilation.GetTypeByMetadataName(
            "Macaron.DependencyInjection.DependencyRegistrarExtensions"
        );
        var owner = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;
        var comparer = SymbolEqualityComparer.Default;

        return method.TypeArguments.Length is 1 or 2
            && (comparer.Equals(owner, registrar)
                || comparer.Equals(owner, runtimeExtensions)
                || comparer.Equals(owner, generatedExtensions)
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
                if (!constructor
                    .GetAttributes()
                    .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, inject))
                )
                {
                    return false;
                }

                return constructor.Parameters.Any(parameter => InjectionParameterValidation.FindAttribute(
                    parameter,
                    compilation,
                    metadataName: "AssistedAttribute"
                ) is not null);
            });
    }
}
