using Microsoft.CodeAnalysis;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.DependencyInjection.Generator;

internal static class TagAttributeValidation
{
    internal static Diagnostic? ValidateParameter(
        AttributeData? tag,
        IParameterSymbol parameter,
        Compilation compilation,
        Location location
    )
    {
        if (HasArrayValue(tag))
        {
            return Diagnostic.Create(
                descriptor: DiagnosticDescriptors.ArrayTagNotSupported,
                location,
                messageArgs: [parameter.Name, parameter.ContainingSymbol.ToDisplayString()]
            );
        }

        return ValidateValueAccessibility(tag, parameter, compilation, location);
    }

    private static Diagnostic? ValidateValueAccessibility(
        AttributeData? tag,
        IParameterSymbol parameter,
        Compilation compilation,
        Location location
    )
    {
        if (tag is not { ConstructorArguments: [{ IsNull: false } value] })
        {
            return null;
        }

        // 원래 상수의 선언 위치가 아니라 생성 코드가 참조할 최종 값의 타입을 검사한다.
        var valueType = value.Kind switch
        {
            TypedConstantKind.Enum => value.Type,
            TypedConstantKind.Type => value.Value as ITypeSymbol,
            _ => null,
        };

        if (valueType is null || compilation.IsSymbolAccessibleWithin(valueType, within: compilation.Assembly))
        {
            return null;
        }

        return Diagnostic.Create(
            descriptor: DiagnosticDescriptors.TagValueTypeNotAccessible,
            location,
            messageArgs: [
                valueType.ToDisplayString(FullyQualifiedFormat),
                parameter.Name,
                parameter.ContainingSymbol.ToDisplayString()
            ]
        );
    }

    private static bool HasArrayValue(AttributeData? attribute)
    {
        return attribute is { ConstructorArguments: [{ Kind: TypedConstantKind.Array }] };
    }
}
