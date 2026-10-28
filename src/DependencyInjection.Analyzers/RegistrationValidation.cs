using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macaron.DependencyInjection.Analysis;

namespace Macaron.DependencyInjection.Analyzers;

internal static class RegistrationValidation
{
    internal static bool IsCandidate(SimpleNameSyntax name)
    {
        return name.Identifier.ValueText is "For" or "Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton";
    }

    internal static ImmutableArray<Diagnostic> ValidateUse(
        SimpleNameSyntax name,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        var compilation = model.Compilation;

        if (model.GetSymbolInfo(name, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var isRegistration = IsRegistrationApi(method, compilation);

        if (!isRegistration && !IsForApi(method, compilation))
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var owner = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

        // 등록 확장 내부의 전달 호출은 소비자의 등록 요청과 중복해서 검사하지 않는다.
        if (SymbolEqualityComparer.Default.Equals(
                model.GetEnclosingSymbol(name.SpanStart, cancellationToken)?.ContainingType,
                owner
            )
        )
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var binding = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
            ? name.Parent
            : name;

        if (binding.Parent is InvocationExpressionSyntax invocation)
        {
            binding = invocation;
        }

        if (model.GetDiagnostics(binding.Span, cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        for (var i = 0; i < method.TypeArguments.Length; i++)
        {
            var type = method.TypeArguments[i];
            var location = name is GenericNameSyntax generic
                ? generic.TypeArgumentList.Arguments[i].GetLocation()
                : name.GetLocation();

            // 소스 선언은 특성 위치에서 한 번 보고하고 외부 DLL의 선언만 사용 위치에서 검사한다.
            if (type is INamedTypeSymbol named && !named.Locations.Any(location => location.IsInSource))
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

            if (isRegistration && InjectionParameterValidation.HasAssistedConstructor(type, compilation)
                && !CompilationValidation.HasConstructorErrors(type, compilation, cancellationToken)
            )
            {
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
        var registrar = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.IDependencyRegistrar");
        var runtimeExtensions = registrar?.ContainingAssembly.GetTypeByMetadataName(
            "Macaron.DependencyInjection.IDependencyRegistrarExtensions"
        );
        var generatedExtensions = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions");
        var owner = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;
        var comparer = SymbolEqualityComparer.Default;

        return method.TypeArguments.Length is 1 or 2
            && (comparer.Equals(owner, registrar)
                || comparer.Equals(owner, runtimeExtensions)
                || comparer.Equals(owner, generatedExtensions)
            );
    }

    private static bool IsForApi(IMethodSymbol method, Compilation compilation)
    {
        var resolver = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.IDependencyResolver");
        var extensions = resolver?.ContainingAssembly.GetTypeByMetadataName(
            "Macaron.DependencyInjection.IDependencyResolverExtensions"
        );

        return method.Name == "For" && method.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(
                (method.ReducedFrom ?? method).OriginalDefinition.ContainingType,
                extensions
            );
    }
}
