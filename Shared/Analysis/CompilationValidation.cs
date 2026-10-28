using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.DependencyInjection.Analysis;

internal static class CompilationValidation
{
    internal static bool HasConstructorErrors(ITypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        if (type is not INamedTypeSymbol implementation)
        {
            return false;
        }

        foreach (var reference in implementation.InstanceConstructors.SelectMany(constructor =>
        {
            return constructor.DeclaringSyntaxReferences;
        }))
        {
            var declaration = reference.GetSyntax(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(reference.SyntaxTree);
            // primary constructor의 구문 참조는 타입 전체를 가리킨다. 다른 멤버의 생성 전 Create 오류가
            // 생성자 선택을 막지 않도록 특성과 매개변수 선언만 검사한다.
            var signatures = declaration is TypeDeclarationSyntax
                ? declaration.ChildNodes().Where(node => node is AttributeListSyntax or ParameterListSyntax)
                : new[] { declaration };

            if (signatures.Any(node => HasErrorDiagnostics(semanticModel, node, cancellationToken)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasErrorDiagnostics(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(node.Span, cancellationToken).Any(diagnostic =>
        {
            return diagnostic.Severity == DiagnosticSeverity.Error && !IsPendingCreateDiagnostic(diagnostic);
        });

        #region Local Functions
        bool IsPendingCreateDiagnostic(Diagnostic diagnostic)
        {
            if (diagnostic.Id is not ("CS1061" or "CS1929" or "CS1501" or "CS1503" or "CS7036" or "CS1739" or "CS0121"))
            {
                return false;
            }

            if (!diagnostic.Location.IsInSource)
            {
                return false;
            }

            var contextType = semanticModel.Compilation.GetTypeByMetadataName(
                "Macaron.DependencyInjection.InjectionContext`1"
            );
            var syntax = node.SyntaxTree.GetRoot(cancellationToken).FindNode(diagnostic.Location.SourceSpan);

            // 발견 단계에는 Create 확장이 없으므로 사용자 팩토리 안의 호출도 생성 후에 바인딩해야 한다.
            // 해당 호출의 오버로드 오류는 생성이 끝난 뒤 컴파일러가 판정하도록 유예한다.
            for (var current = syntax; current is not null; current = current.Parent)
            {
                var access = current switch
                {
                    MemberAccessExpressionSyntax member => member,
                    InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member,
                    _ => null,
                };

                if (access is not { Name.Identifier.ValueText: "Create" })
                {
                    continue;
                }

                if (semanticModel.GetTypeInfo(access.Expression, cancellationToken).Type is INamedTypeSymbol receiver
                    && SymbolEqualityComparer.Default.Equals(receiver.OriginalDefinition, contextType)
                )
                {
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}
