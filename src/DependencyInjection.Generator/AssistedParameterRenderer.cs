using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.KeywordNameHelpers;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using static Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions;

namespace Macaron.DependencyInjection.Generator;

internal static class AssistedParameterRenderer
{
    internal static readonly SymbolDisplayFormat SignatureTypeFormat = FullyQualifiedFormat.WithMiscellaneousOptions(
        FullyQualifiedFormat.MiscellaneousOptions
        | IncludeNullableReferenceTypeModifier
    );

    internal static string Render(IParameterSymbol parameter, Compilation compilation)
    {
        var type = parameter.Type.ToDisplayString(SignatureTypeFormat);
        var modifier = parameter.IsParams ? "params " : parameter.RefKind == RefKind.In ? "in " : "";
        var declaration = $"{modifier}{type} {EscapeName(parameter.Name)}";

        if (!parameter.HasExplicitDefaultValue)
        {
            return declaration;
        }

        var expression = ParameterDefaultValueRenderer.Render(parameter, compilation);

        return $"{declaration} = {expression}";
    }
}
