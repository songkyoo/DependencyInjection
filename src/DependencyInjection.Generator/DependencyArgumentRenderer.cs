using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.AssistedParameterRenderer;

namespace Macaron.DependencyInjection.Generator;

internal static class DependencyArgumentRenderer
{
    internal static string Render(
        IParameterSymbol parameter,
        string resolverName,
        string tagArguments,
        Compilation compilation
    )
    {
        var parameterType = parameter.Type;
        var serviceType = parameterType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        if (!parameter.HasExplicitDefaultValue)
        {
            return $"{resolverName}.Resolve<{serviceType.ToDisplayString(SignatureTypeFormat)}>({tagArguments})";
        }

        if (parameterType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        } nullableType)
        {
            serviceType = nullableType.TypeArguments[0];
        }

        var serviceTypeName = serviceType.ToDisplayString(SignatureTypeFormat);
        var resolveExpression = $"{resolverName}.Resolve<{serviceTypeName}>({tagArguments})";
        var parameterTypeName = parameterType.ToDisplayString(SignatureTypeFormat);
        var fallback = ParameterDefaultValueRenderer.Render(parameter, compilation);

        // 선언에 명시된 null 기본값은 non-nullable 매개변수에도 그대로 전달한다.
        if (fallback == "null" && parameterType.IsReferenceType)
        {
            fallback = "null!";
        }

        // 두 분기를 원래 매개변수 타입으로 맞춰 값/in 생성자의 오버로드 선택을 보존한다.
        return $"{resolverName}.Contains<{serviceTypeName}>({tagArguments})"
            + $" ? ({parameterTypeName}){resolveExpression}"
            + $" : ({parameterTypeName}){fallback}";
    }
}
