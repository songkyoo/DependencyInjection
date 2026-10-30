using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Macaron.DependencyInjection.Analysis;

internal static class GenerationApiSelection
{
    internal static bool IsCandidate(SimpleNameSyntax name)
    {
        return name.Identifier.ValueText
            is "For"
            or "Register"
            or "RegisterTransient"
            or "RegisterScoped"
            or "RegisterSingleton";
    }

    internal static SyntaxNode GetBinding(SimpleNameSyntax name)
    {
        var binding = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
            ? name.Parent
            : name;

        return binding.Parent as InvocationExpressionSyntax ?? binding;
    }

    internal static IMethodSymbol? GetBoundMethod(
        SimpleNameSyntax name,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        var binding = GetBinding(name);

        if (model.GetSymbolInfo(binding, cancellationToken).Symbol is IMethodSymbol method)
        {
            return method;
        }

        // 확장 메서드 그룹은 GetSymbolInfo가 실패해도 델리게이트로 바인딩될 수 있다.
        // 후보 심벌을 추측하지 않고 컴파일러가 선택한 메서드 참조만 사용한다.
        return ExtractMethodForDelegateCreation(binding, model, cancellationToken) is { } reference
            ? reference.Method
            : null;

        #region Local Functions
        static IMethodReferenceOperation? ExtractMethodForDelegateCreation(
            SyntaxNode binding,
            SemanticModel model,
            CancellationToken cancellationToken
        )
        {
            if (binding is InvocationExpressionSyntax)
            {
                return null;
            }

            var operation = model.GetOperation(binding, cancellationToken);

            return operation is IMethodReferenceOperation { Parent: IDelegateCreationOperation } reference
                ? reference
                : null;
        }
        #endregion
    }

    internal static int GetTargetTypeArgumentIndex(IMethodSymbol method, Compilation compilation)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (!definition.IsExtensionMethod || definition.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
        {
            return -1;
        }

        var comparer = SymbolEqualityComparer.Default;

        if (definition.Name == "For")
        {
            var resolver = compilation.GetTypeByMetadataName(
                "Macaron.DependencyInjection.IDependencyResolver"
            );
            var extensions = resolver?.ContainingAssembly.GetTypeByMetadataName(
                "Macaron.DependencyInjection.IDependencyResolverExtensions"
            );
            var context = resolver?.ContainingAssembly.GetTypeByMetadataName(
                "Macaron.DependencyInjection.InjectionContext`1"
            );

            return definition.Arity == 1
                && comparer.Equals(definition.ContainingType, extensions)
                && definition.Parameters.Length == 1
                && comparer.Equals(definition.Parameters[0].Type, resolver)
                && definition.ReturnType is INamedTypeSymbol result
                && comparer.Equals(result.OriginalDefinition, context)
                && comparer.Equals(result.TypeArguments[0], definition.TypeParameters[0])
                ? 0
                : -1;
        }

        if (definition.Name is not ("Register" or "RegisterTransient" or "RegisterScoped" or "RegisterSingleton")
            || definition.Arity is not (1 or 2)
            || !definition.ReturnsVoid
        )
        {
            return -1;
        }

        var registrar = compilation.GetTypeByMetadataName(
            "Macaron.DependencyInjection.IDependencyRegistrar"
        );
        var generatedExtensions = compilation.GetTypeByMetadataName(
            "Macaron.DependencyInjection.DependencyRegistrarExtensions"
        );
        var hasLifetime = definition.Name == "Register";
        var tagIndex = hasLifetime ? 2 : 1;

        // 원시 API와 런타임 확장은 사용자가 생성 방법을 제공하므로 검사하지 않는다.
        if (!comparer.Equals(definition.ContainingType, generatedExtensions)
            || !comparer.Equals(definition.ContainingAssembly, compilation.Assembly)
            || definition.Parameters.Length != tagIndex + 2
            || !comparer.Equals(definition.Parameters[0].Type, registrar)
        )
        {
            return -1;
        }

        if (hasLifetime && !comparer.Equals(
            definition.Parameters[1].Type,
            registrar.ContainingAssembly.GetTypeByMetadataName("Macaron.DependencyInjection.Lifetime")
        ))
        {
            return -1;
        }

        var tag = definition.Parameters[tagIndex];
        var cleanup = definition.Parameters[tagIndex + 1];
        var action = compilation.GetTypeByMetadataName("System.Action`1");

        return tag.Type.SpecialType == SpecialType.System_Object
            && tag.IsOptional
            && cleanup is { IsOptional: true, Type: INamedTypeSymbol callback }
            && comparer.Equals(callback.OriginalDefinition, action)
            && comparer.Equals(callback.TypeArguments[0], definition.TypeParameters[0])
            ? definition.Arity - 1
            : -1;
    }
}
