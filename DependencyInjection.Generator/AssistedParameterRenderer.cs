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

    internal static string Render(IParameterSymbol parameter)
    {
        var type = parameter.Type.ToDisplayString(SignatureTypeFormat);
        var modifier = parameter.IsParams ? "params " : parameter.RefKind == RefKind.In ? "in " : "";
        var declaration = $"{modifier}{type} {EscapeName(parameter.Name)}";

        if (!parameter.HasExplicitDefaultValue)
        {
            return declaration;
        }

        if (parameter.ExplicitDefaultValue is not { } value)
        {
            return $"{declaration} = default({type})";
        }

        var literal = ConstantValueRenderer.Render(value);

        return $"{declaration} = ({type})({literal})";
    }
}
