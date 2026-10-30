using Microsoft.CodeAnalysis;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Analysis.InjectionParameterValidation;
using static Macaron.DependencyInjection.Generator.KeywordNameHelpers;
using static Macaron.DependencyInjection.Generator.Result;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal sealed class InjectionArgumentsBuilder
{
    private readonly IMethodSymbol _member;
    private readonly Compilation _compilation;
    private readonly HashSet<string> _reservedNames;

    internal InjectionArgumentsBuilder(IMethodSymbol member, Compilation compilation)
    {
        _member = member;
        _compilation = compilation;
        _reservedNames = new HashSet<string>(member
            .Parameters
            .Where(parameter => FindAttribute(parameter, compilation, metadataName: "AssistedAttribute") is not null)
            .Select(parameter => parameter.Name)
        );
    }

    internal Result<InjectionArguments, Diagnostic> Build(Location useLocation, string resolverName)
    {
        var arguments = new List<string>();
        var dependencyStatements = new List<string>();
        var assistedParameters = new List<string>();
        var usesInParameters = _member.Parameters.Any(parameter => parameter.RefKind == RefKind.In);

        foreach (var parameter in _member.Parameters)
        {
            var parameterType = parameter.Type;
            var tag = FindAttribute(parameter, _compilation, metadataName: "TagAttribute");
            var assisted = FindAttribute(parameter, _compilation, metadataName: "AssistedAttribute");
            var attributeName = assisted is not null
                ? "Assisted"
                : tag is not null ? "Tag" : "dependency injection";
            var parameterDiagnostic = ValidateShape(
                parameter,
                attributeName,
                location: useLocation,
                additionalLocations: parameter.Locations
            );

            if (parameterDiagnostic is not null)
            {
                return Error(parameterDiagnostic);
            }

            if (!_compilation.IsSymbolAccessibleWithin(parameterType, _compilation.Assembly))
            {
                return Error(Diagnostic.Create(
                    descriptor: DiagnosticDescriptors.DependencyNotAccessible,
                    location: useLocation,
                    additionalLocations: parameter.Locations.Where(location => location.IsInSource),
                    properties: null,
                    messageArgs:
                    [
                        parameterType.ToDisplayString(FullyQualifiedFormat),
                        parameter.Name,
                        _member.ContainingType.ToDisplayString(FullyQualifiedFormat),
                        _member.MethodKind == MethodKind.Constructor ? "constructor" : "method",
                    ]
                ));
            }

            if (assisted is not null)
            {
                assistedParameters.Add(AssistedParameterRenderer.Render(parameter, _compilation));

                var name = EscapeName(parameter.Name);

                arguments.Add(parameter.RefKind == RefKind.In ? $"in {name}" : name);

                continue;
            }

            var resolveArguments = tag is { ConstructorArguments.Length: 1 }
                ? $"tag: {TagValueRenderer.Render(tag.ConstructorArguments[0], _compilation)}"
                : "";
            var resolveExpression = DependencyArgumentRenderer.Render(
                parameter,
                resolverName,
                tagArguments: resolveArguments,
                compilation: _compilation
            );

            if (usesInParameters)
            {
                var localTypeName = parameterType.ToDisplayString(AssistedParameterRenderer.SignatureTypeFormat);
                var variableName = ReserveName($"dependency{arguments.Count}");

                dependencyStatements.Add($"{localTypeName} {variableName} = {resolveExpression};");
                arguments.Add(parameter.RefKind == RefKind.In ? $"in {variableName}" : variableName);
            }
            else
            {
                arguments.Add(resolveExpression);
            }
        }

        return Ok(new InjectionArguments(
            Arguments: string.Join(", ", arguments),
            DependencyStatements: string.Join("\n", dependencyStatements),
            AssistedParameters: string.Join(", ", assistedParameters)
        ));
    }

    internal string ReserveName(string name)
    {
        var candidate = name;
        var suffix = 1;

        while (!_reservedNames.Add(candidate))
        {
            candidate = name + suffix++;
        }

        return candidate;
    }
}
