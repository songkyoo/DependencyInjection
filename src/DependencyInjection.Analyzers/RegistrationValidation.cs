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
        InjectionAnalysisModels models,
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

        if (model.GetSymbolInfo(binding, cancellationToken).Symbol is not IMethodSymbol)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        model = models.ForTypes(model, method.TypeArguments);
        compilation = model.Compilation;
        method = (IMethodSymbol)model.GetSymbolInfo(name, cancellationToken).Symbol!;

        for (var i = 0; i < method.TypeArguments.Length; i++)
        {
            var type = method.TypeArguments[i];
            var location = name is GenericNameSyntax generic
                ? generic.TypeArgumentList.Arguments[i].GetLocation()
                : name.GetLocation();

            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            var declaration = InjectionDeclarationAnalysis.Analyze(named, compilation, location, cancellationToken);

            // 소스 선언은 선언 위치에 한 번 보고하고 외부 선언은 각 사용 위치에서 검사한다.
            if (!named.Locations.Any(location => location.IsInSource))
            {
                diagnostics.AddRange(declaration.Diagnostics);
            }

            if (!isRegistration || declaration.HasErrors || declaration.Member is not { } member)
            {
                continue;
            }

            DiagnosticDescriptor? descriptor = null;

            if (member.MethodKind != MethodKind.Constructor)
            {
                descriptor = DiagnosticDescriptors.MethodInjectedTypeCannotBeRegistered;
            }
            else if (member.Parameters.Any(parameter => InjectionParameterValidation.FindAttribute(
                parameter,
                compilation,
                metadataName: "AssistedAttribute"
            ) is not null))
            {
                descriptor = DiagnosticDescriptors.AssistedTypeCannotBeRegistered;
            }

            if (descriptor is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    descriptor,
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
