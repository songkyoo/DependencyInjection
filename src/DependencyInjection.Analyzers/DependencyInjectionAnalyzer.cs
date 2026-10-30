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
        DiagnosticDescriptors.MultipleInjectMembers,
        DiagnosticDescriptors.UnsupportedParameter,
        DiagnosticDescriptors.ArrayTagNotSupported,
        DiagnosticDescriptors.RequiresInject,
        DiagnosticDescriptors.TagValueTypeNotAccessible,
        DiagnosticDescriptors.ConflictingParameterAttributes,
        DiagnosticDescriptors.AssistedTypeCannotBeRegistered,
        DiagnosticDescriptors.UnsupportedInjectionMethod,
        DiagnosticDescriptors.MethodInjectedTypeCannotBeRegistered,
        DiagnosticDescriptors.InjectionMemberNotAccessible
    );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var models = new InjectionAnalysisModels(compilation);

            start.RegisterSymbolAction(
                action: analysis =>
                {
                    var diagnostics = InjectionDeclarationValidation.ValidateSource(
                        type: (INamedTypeSymbol)analysis.Symbol,
                        compilation,
                        cancellationToken: analysis.CancellationToken
                    );

                    foreach (var diagnostic in diagnostics)
                    {
                        analysis.ReportDiagnostic(diagnostic);
                    }
                },
                symbolKinds: SymbolKind.NamedType
            );

            start.RegisterSyntaxNodeAction(
                action: analysis =>
                {
                    var attribute = (AttributeSyntax)analysis.Node;

                    if (attribute.Parent?.Parent is not (ParameterSyntax or LocalFunctionStatementSyntax))
                    {
                        return;
                    }

                    var model = analysis.SemanticModel;
                    var sourceCompilation = model.Compilation;

                    if (attribute.Parent.Parent is LocalFunctionStatementSyntax local
                        && model.GetDeclaredSymbol(
                            local,
                            cancellationToken: analysis.CancellationToken
                        ) is IMethodSymbol member
                        && InjectionParameterValidation
                            .FindInjectAttribute(member, sourceCompilation)?
                            .ApplicationSyntaxReference?
                            .Span == analysis.Node.Span
                        && !CompilationValidation.HasDeclarationErrors(
                            model,
                            local,
                            cancellationToken: analysis.CancellationToken
                        )
                    )
                    {
                        var diagnostic = InjectionDeclarationAnalysis.ValidateMethodShape(
                            method: member,
                            compilation: sourceCompilation,
                            location: local.Identifier.GetLocation()
                        );

                        if (diagnostic is not null)
                        {
                            analysis.ReportDiagnostic(diagnostic);
                        }
                    }

                    if (attribute.Parent.Parent is not ParameterSyntax)
                    {
                        return;
                    }

                    var diagnostics = InjectionParameterValidation.ValidateDeclaration(
                        syntax: attribute,
                        semanticModel: model,
                        cancellationToken: analysis.CancellationToken
                    );

                    foreach (var diagnostic in diagnostics)
                    {
                        analysis.ReportDiagnostic(diagnostic);
                    }
                },
                syntaxKinds: SyntaxKind.Attribute
            );

            start.RegisterSyntaxNodeAction(
                action: analysis =>
                {
                    var name = (SimpleNameSyntax)analysis.Node;

                    if (!RegistrationValidation.IsCandidate(name))
                    {
                        return;
                    }

                    var model = analysis.SemanticModel;
                    var diagnostics = RegistrationValidation.ValidateUse(
                        name,
                        model,
                        models,
                        cancellationToken: analysis.CancellationToken
                    );

                    foreach (var diagnostic in diagnostics)
                    {
                        analysis.ReportDiagnostic(diagnostic);
                    }
                },
                syntaxKinds: ImmutableArray.Create(SyntaxKind.IdentifierName, SyntaxKind.GenericName)
            );
        });
    }
}
