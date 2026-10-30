using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macaron.DependencyInjection.Analysis;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using static Microsoft.CodeAnalysis.TypeKind;

namespace Macaron.DependencyInjection.Generator;

internal static class InjectionEntryPointValidation
{
    internal static bool IsCandidate(SyntaxNode node)
    {
        return node is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" or "Inject" };
    }

    internal static ImmutableArray<Diagnostic> Validate(
        ImmutableArray<SyntaxReference> references,
        Compilation sourceCompilation,
        CancellationToken cancellationToken
    )
    {
        var comparer = SymbolEqualityComparer.Default;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var models = new InjectionAnalysisModels(sourceCompilation);

        foreach (var reference in references)
        {
            var access = (MemberAccessExpressionSyntax)reference.GetSyntax(cancellationToken);
            var model = sourceCompilation.GetSemanticModel(access.SyntaxTree);

            if (model.GetTypeInfo(access.Expression, cancellationToken).Type is not INamedTypeSymbol originalReceiver)
            {
                continue;
            }

            model = models.ForTypes(model, originalReceiver.TypeArguments);

            var compilation = model.Compilation;
            var context = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContext`1");

            if (model.GetTypeInfo(access.Expression, cancellationToken).Type is not INamedTypeSymbol receiver
                || !comparer.Equals(receiver.OriginalDefinition, context)
                || receiver.TypeArguments[0] is not INamedTypeSymbol { TypeKind: Class or Struct } type
                || type.IsAbstract
                || type.IsStatic
                || type.IsRefLikeType
                || !ConstructionDiscovery.IsClosed(type)
                || InjectionParameterValidation.ContainsType(type, kind: Dynamic)
                || !compilation.IsSymbolAccessibleWithin(type, within: compilation.Assembly)
            )
            {
                continue;
            }

            if (model.GetSymbolInfo(access, cancellationToken).Symbol is IMethodSymbol boundMethod
                && !comparer.Equals(
                    (boundMethod.ReducedFrom ?? boundMethod).ContainingType,
                    compilation.GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")
                )
            )
            {
                continue;
            }

            var declaration = InjectionDeclarationAnalysis.Analyze(type, compilation, access.Name.GetLocation(), cancellationToken);

            if (declaration.HasErrors)
            {
                continue;
            }

            var methodInjection = declaration.Member is { MethodKind: not MethodKind.Constructor };

            if (!methodInjection && declaration.Member is null
                && type.InstanceConstructors.Count(constructor =>
                {
                    return compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly);
                }) != 1
            )
            {
                // 자동 생성자 선택의 실패는 For 분석에서 보고한다.
                continue;
            }

            var expectedName = methodInjection ? "Inject" : "Create";

            if (access.Name.Identifier.ValueText != expectedName)
            {
                diagnostics.Add(Diagnostic.Create(
                    descriptor: DiagnosticDescriptors.InjectionEntryPointMismatch,
                    location: access.Name.GetLocation(),
                    messageArgs:
                    [
                        type.ToDisplayString(FullyQualifiedFormat),
                        methodInjection ? "method injection" : "constructor injection",
                        expectedName,
                        access.Name.Identifier.ValueText,
                    ]
                ));
            }
        }

        return diagnostics.ToImmutable();
    }
}
