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

        if (parameter.ExplicitDefaultValue is not { } value)
        {
            var defaultExpression = parameter.Type.IsReferenceType
                || parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";

            return $"{declaration} = {defaultExpression}";
        }

        var valueType = parameter.Type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullableType
            ? nullableType.TypeArguments[0]
            : parameter.Type;
        var expression = valueType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? EnumValueRenderer.Render(enumType, value, compilation)
            : ConstantValueRenderer.Render(value);

        return $"{declaration} = {expression}";
    }
}
