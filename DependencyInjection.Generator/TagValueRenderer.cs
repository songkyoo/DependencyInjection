using Microsoft.CodeAnalysis;

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
        var literal = ConstantValueRenderer.Render(value.Value!);

        // object 태그의 동등성은 boxing 타입도 구분하므로 숫자와 enum의 원래 타입을 유지한다.
        return $"({typeName})({literal})";
    }
}
