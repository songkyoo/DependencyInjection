using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Analysis;

internal static class InjectionDeclarationAnalysis
{
    internal static InjectionDeclaration Analyze(
        INamedTypeSymbol type,
        Compilation compilation,
        Location useLocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // GetMembers는 T의 직접 선언만 반환한다.
        var members = type.GetMembers().OfType<IMethodSymbol>().ToArray();

        if (members.Any(member => CompilationValidation.HasMemberErrors(member, compilation, cancellationToken)))
        {
            return new InjectionDeclaration(
                Member: null,
                Diagnostics: ImmutableArray<Diagnostic>.Empty,
                HasCompilerErrors: true
            );
        }

        var marked = members.Where(member => FindInjectAttribute(member, compilation) is not null).ToArray();

        if (marked.Length > 1)
        {
            var locations = marked
                .Select(member =>
                {
                    var attribute = FindInjectAttribute(member, compilation)!;

                    return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                })
                .Where(location => location is not null)
                .Select(location => location!)
                .ToArray();
            var location = locations.LastOrDefault() ?? useLocation;
            var descriptor = marked.All(member => member.MethodKind == MethodKind.Constructor)
                ? DiagnosticDescriptors.MultipleInjectConstructors
                : DiagnosticDescriptors.MultipleInjectMembers;
            var diagnostic = Diagnostic.Create(
                descriptor,
                location,
                additionalLocations: locations.Where(candidate => candidate.IsInSource && !candidate.Equals(location)),
                properties: null,
                messageArgs: [type.ToDisplayString(FullyQualifiedFormat)]
            );

            return new InjectionDeclaration(
                Member: null,
                Diagnostics: ImmutableArray.Create(diagnostic),
                HasCompilerErrors: false
            );
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var member in members)
        {
            var isMarked = FindInjectAttribute(member, compilation) is not null;
            var location = member.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? useLocation;
            var diagnostic = isMarked && member.MethodKind != MethodKind.Constructor
                ? ValidateMethodShape(member, compilation, location)
                : null;

            diagnostic ??= ValidateMemberParameters(member, compilation, useLocation);

            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        return new InjectionDeclaration(
            Member: marked.SingleOrDefault(),
            Diagnostics: diagnostics.ToImmutable(),
            HasCompilerErrors: false
        );
    }

    internal static Diagnostic? ValidateMethodUse(
        IMethodSymbol method,
        Compilation compilation,
        Location useLocation
    )
    {
        var shapeDiagnostic = ValidateMethodShape(method, compilation, useLocation);

        if (shapeDiagnostic is not null)
        {
            return shapeDiagnostic;
        }

        if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
        {
            return Diagnostic.Create(
                descriptor: DiagnosticDescriptors.InjectionMethodNotAccessible,
                location: useLocation,
                additionalLocations: method.Locations.Where(location => location.IsInSource),
                properties: null,
                messageArgs: [method.ToDisplayString(), method.ContainingType.ToDisplayString(FullyQualifiedFormat)]
            );
        }

        foreach (var parameter in method.Parameters)
        {
            if (!compilation.IsSymbolAccessibleWithin(parameter.Type, compilation.Assembly))
            {
                return Diagnostic.Create(
                    descriptor: DiagnosticDescriptors.DependencyNotAccessible,
                    location: useLocation,
                    additionalLocations: parameter.Locations.Where(location => location.IsInSource),
                    properties: null,
                    messageArgs:
                    [
                        parameter.Type.ToDisplayString(FullyQualifiedFormat),
                        parameter.Name,
                        method.ContainingType.ToDisplayString(FullyQualifiedFormat),
                        "method",
                        "injection",
                    ]
                );
            }
        }

        return null;
    }

    private static Diagnostic? ValidateMethodShape(IMethodSymbol method, Compilation compilation, Location location)
    {
        // 외부 DLL의 async void도 구분하도록 컴파일러가 남긴 상태 머신 특성을 함께 검사한다.
        var asyncAttribute = compilation.GetTypeByMetadataName(
            "System.Runtime.CompilerServices.AsyncStateMachineAttribute"
        );
        var isAsync = method.IsAsync || method.GetAttributes().Any(attribute =>
        {
            return asyncAttribute is not null
                && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, asyncAttribute);
        });

        if (method is { MethodKind: MethodKind.Ordinary, IsStatic: false, IsGenericMethod: false }
            && !isAsync && method.ReturnsVoid
        )
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.UnsupportedInjectionMethod,
            location,
            messageArgs: [method.ToDisplayString()]
        );
    }
}
