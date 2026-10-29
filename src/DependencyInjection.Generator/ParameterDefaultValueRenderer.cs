using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Generator;

internal static class ParameterDefaultValueRenderer
{
    internal static string Render(IParameterSymbol parameter, Compilation compilation)
    {
        if (parameter.ExplicitDefaultValue is not { } value)
        {
            return parameter.Type.IsReferenceType
                || parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        var valueType = parameter.Type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullableType
            ? nullableType.TypeArguments[0]
            : parameter.Type;

        return valueType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? EnumValueRenderer.Render(enumType, value, compilation)
            : ConstantValueRenderer.Render(value);
    }
}
