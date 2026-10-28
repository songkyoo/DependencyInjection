using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Macaron.DependencyInjection.Analysis;

namespace Macaron.DependencyInjection.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyInjectionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        DiagnosticDescriptors.DynamicTypeNotSupported,
        DiagnosticDescriptors.UnsupportedParameter,
        DiagnosticDescriptors.ArrayTagNotSupported,
        DiagnosticDescriptors.RequiresInject,
        DiagnosticDescriptors.TagValueTypeNotAccessible,
        DiagnosticDescriptors.ConflictingParameterAttributes,
        DiagnosticDescriptors.AssistedTypeCannotBeRegistered
    );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var input = (CSharpCompilation)start.Compilation;

            // 외부 DLL의 비공개 생성자도 같은 선언 규칙으로 검사한다.
            var compilation = input.WithOptions(input.Options.WithMetadataImportOptions(MetadataImportOptions.All));

            start.RegisterSyntaxNodeAction(
                analysis =>
                {
                    if (analysis.Node is not AttributeSyntax { Parent.Parent: ParameterSyntax } attribute)
                    {
                        return;
                    }

                    var diagnostics = InjectionParameterValidation.ValidateDeclaration(
                        syntax: attribute,
                        semanticModel: analysis.SemanticModel,
                        cancellationToken: analysis.CancellationToken
                    );

                    foreach (var diagnostic in diagnostics)
                    {
                        analysis.ReportDiagnostic(diagnostic);
                    }
                },
                SyntaxKind.Attribute
            );

            start.RegisterSyntaxNodeAction(
                analysis =>
                {
                    var name = (SimpleNameSyntax)analysis.Node;

                    if (!RegistrationValidation.IsCandidate(name))
                    {
                        return;
                    }

                    // 외부 DLL의 비공개 생성자는 호스트가 제공한 기본 모델에서 생략될 수 있다.
#pragma warning disable RS1030
                    var model = compilation.GetSemanticModel(name.SyntaxTree);
#pragma warning restore RS1030
                    var diagnostics = RegistrationValidation.ValidateUse(name, model, analysis.CancellationToken);

                    foreach (var diagnostic in diagnostics)
                    {
                        analysis.ReportDiagnostic(diagnostic);
                    }
                },
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName
            );
        });
    }
}
