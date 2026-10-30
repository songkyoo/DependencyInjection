using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Analysis;

internal static class InjectionDeclarationAnalysis
{
    internal static IEnumerable<IMethodSymbol> GetAccessibleMembers(INamedTypeSymbol type, Compilation compilation)
    {
        return type.GetMembers().OfType<IMethodSymbol>().Where(member =>
        {
            return compilation.IsSymbolAccessibleWithin(member, compilation.Assembly);
        });
    }

    internal static bool HasMethodInjection(ITypeSymbol type, Compilation compilation)
    {
        return type is INamedTypeSymbol named && GetAccessibleMembers(named, compilation).Any(member =>
        {
            return member.MethodKind != MethodKind.Constructor && FindInjectAttribute(member, compilation) is not null;
        });
    }

    internal static InjectionDeclaration Analyze(
        INamedTypeSymbol type,
        Compilation compilation,
        Location useLocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 접근성은 경로를 바꾸지 않는다. T 자체의 모든 Inject 선언으로 경로를 먼저 정한다.
        var members = type.GetMembers().OfType<IMethodSymbol>().Where(member =>
        {
            return FindInjectAttribute(member, compilation) is not null
                || compilation.IsSymbolAccessibleWithin(member, compilation.Assembly);
        }).ToArray();

        if (members.Any(member => CompilationValidation.HasMemberDeclarationErrors(member, compilation, cancellationToken)))
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
            var diagnostic = Diagnostic.Create(
                descriptor: DiagnosticDescriptors.MultipleInjectMembers,
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

        var selected = marked.SingleOrDefault();

        if (selected is not null && !compilation.IsSymbolAccessibleWithin(selected, compilation.Assembly))
        {
            var attribute = FindInjectAttribute(selected, compilation)!;
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? useLocation;
            var diagnostic = Diagnostic.Create(
                descriptor: DiagnosticDescriptors.InjectionMemberNotAccessible,
                location,
                messageArgs: [selected.ToDisplayString()]
            );

            return new InjectionDeclaration(
                Member: selected,
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
            Member: selected,
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
                    ]
                );
            }
        }

        return null;
    }

    internal static Diagnostic? ValidateMethodShape(IMethodSymbol method, Compilation compilation, Location location)
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
