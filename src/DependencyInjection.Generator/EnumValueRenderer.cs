using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.KeywordNameHelpers;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class EnumValueRenderer
{
    internal static string Render(INamedTypeSymbol enumType, object value, Compilation compilation)
    {
        var typeName = enumType.ToDisplayString(FullyQualifiedFormat);
        var obsoleteAttribute = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
        var member = enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field =>
            {
                return field is { IsStatic: true, HasConstantValue: true }
                    && Equals(field.ConstantValue, value)
                    && compilation.IsSymbolAccessibleWithin(field, within: compilation.Assembly)
                    && !field.GetAttributes().Any(attribute =>
                    {
                        return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute);
                    });
            })
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        // 같은 값을 가진 멤버의 선언 순서가 바뀌어도 같은 표현을 선택하도록 이름 순으로 결정한다.
        if (member is not null)
        {
            return $"{typeName}.{EscapeName(member.Name)}";
        }

        return $"({typeName})({ConstantValueRenderer.Render(value)})";
    }
}
