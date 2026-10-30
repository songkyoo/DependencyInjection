using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macaron.DependencyInjection.Analysis;

namespace Macaron.DependencyInjection.Analyzers;

internal static class RegistrationValidation
{
    internal static bool IsCandidate(SimpleNameSyntax name)
    {
        return GenerationApiSelection.IsCandidate(name);
    }

    internal static ImmutableArray<Diagnostic> ValidateUse(
        SimpleNameSyntax name,
        SemanticModel model,
        InjectionAnalysisModels models,
        CancellationToken cancellationToken
    )
    {
        var compilation = model.Compilation;

        if (GenerationApiSelection.GetBoundMethod(name, model, cancellationToken) is not { } method)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var targetIndex = GenerationApiSelection.GetTargetTypeArgumentIndex(method, compilation);

        if (targetIndex < 0)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var owner = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

        // 등록 확장 내부의 전달 호출은 소비자의 등록 요청과 중복해서 검사하지 않는다.
        if (SymbolEqualityComparer.Default.Equals(
            model.GetEnclosingSymbol(name.SpanStart, cancellationToken)?.ContainingType,
            owner
        ))
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        model = models.ForTypes(model, types: [method.TypeArguments[targetIndex]]);
        compilation = model.Compilation;
        method = GenerationApiSelection.GetBoundMethod(name, model, cancellationToken)!;

        var type = method.TypeArguments[targetIndex];
        var location = name is GenericNameSyntax generic
            ? generic.TypeArgumentList.Arguments[targetIndex].GetLocation()
            : name.GetLocation();

        if (type is not INamedTypeSymbol named)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var declaration = InjectionDeclarationAnalysis.Analyze(named, compilation, location, cancellationToken);

        // 소스 선언은 선언 위치에 한 번 보고하고 외부 선언은 각 사용 위치에서 검사한다.
        if (!named.Locations.Any(location => location.IsInSource))
        {
            diagnostics.AddRange(declaration.Diagnostics);
        }

        if (method.Name == "For" || declaration.HasErrors || declaration.Member is not { } member)
        {
            return diagnostics.ToImmutable();
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

        return diagnostics.ToImmutable();
    }
}
