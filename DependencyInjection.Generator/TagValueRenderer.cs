using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class TagValueRenderer
{
    internal static string Render(TypedConstant value)
    {
        if (value.IsNull)
        {
            return "null";
        }

        if (value.Kind == TypedConstantKind.Type)
        {
            var type = (ITypeSymbol)value.Value!;

            return $"typeof({type.ToDisplayString(FullyQualifiedFormat)})";
        }

        var typeName = value.Type!.ToDisplayString(FullyQualifiedFormat);
        var literal = value.Value switch
        {
            float number when float.IsNaN(number) => "float.NaN",
            float number when float.IsPositiveInfinity(number) => "float.PositiveInfinity",
            float number when float.IsNegativeInfinity(number) => "float.NegativeInfinity",
            double number when double.IsNaN(number) => "double.NaN",
            double number when double.IsPositiveInfinity(number) => "double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "double.NegativeInfinity",
            _ => SymbolDisplay.FormatPrimitive(value.Value!, quoteStrings: true, useHexadecimalNumbers: false),
        };

        return $"({typeName})({literal})";
    }
}
