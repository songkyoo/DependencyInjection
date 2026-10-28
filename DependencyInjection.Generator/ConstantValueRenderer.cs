using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.DependencyInjection.Generator;

internal static class ConstantValueRenderer
{
    internal static string Render(object value)
    {
        return value switch
        {
            float number when float.IsNaN(number) => "float.NaN",
            float number when float.IsPositiveInfinity(number) => "float.PositiveInfinity",
            float number when float.IsNegativeInfinity(number) => "float.NegativeInfinity",
            float number => SymbolDisplay.FormatPrimitive(number, quoteStrings: true, useHexadecimalNumbers: false) + "F",
            double number when double.IsNaN(number) => "double.NaN",
            double number when double.IsPositiveInfinity(number) => "double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "double.NegativeInfinity",
            double number => SymbolDisplay.FormatPrimitive(number, quoteStrings: true, useHexadecimalNumbers: false) + "D",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            _ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
        };
    }
}
