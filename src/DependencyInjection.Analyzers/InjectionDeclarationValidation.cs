using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;

namespace Macaron.DependencyInjection.Analyzers;

internal static class InjectionDeclarationValidation
{
    internal static ImmutableArray<Diagnostic> ValidateSource(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var location = type.Locations.FirstOrDefault(location => location.IsInSource);

        if (type.IsImplicitlyDeclared || location is null)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var declaration = InjectionDeclarationAnalysis.Analyze(type, compilation, location, cancellationToken);

        if (declaration.HasCompilerErrors)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        // 특성이 있는 매개변수는 AttributeSyntax 검사에서 각 오류를 독립적으로 보고한다.
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        diagnostics.AddRange(declaration.Diagnostics.Where(diagnostic =>
        {
            return diagnostic.Descriptor.Equals(DiagnosticDescriptors.MultipleInjectMembers)
                || diagnostic.Descriptor.Equals(DiagnosticDescriptors.UnsupportedInjectionMethod)
                || diagnostic.Descriptor.Equals(DiagnosticDescriptors.InjectionMemberNotAccessible);
        }));

        foreach (var member in InjectionDeclarationAnalysis.GetAccessibleMembers(type, compilation))
        {
            if (member.MethodKind == MethodKind.Constructor || FindInjectAttribute(member, compilation) is null)
            {
                continue;
            }

            foreach (var parameter in member.Parameters)
            {
                if (FindAttribute(parameter, compilation, metadataName: "TagAttribute") is not null
                    || FindAttribute(parameter, compilation, metadataName: "AssistedAttribute") is not null
                )
                {
                    continue;
                }

                var diagnostic = ValidateShape(
                    parameter,
                    attributeName: "dependency injection",
                    location: parameter.Locations.FirstOrDefault(location => location.IsInSource) ?? location
                );

                if (diagnostic is not null)
                {
                    diagnostics.Add(diagnostic);
                }
            }
        }

        return diagnostics.ToImmutable();
    }
}
