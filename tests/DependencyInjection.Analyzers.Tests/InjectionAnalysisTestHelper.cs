using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

internal static class InjectionAnalysisTestHelper
{
    private const string Attributes =
        """
        #nullable enable

        using System;

        namespace Macaron.DependencyInjection
        {
            [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
            public sealed class InjectAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class TagAttribute : Attribute
            {
                public object? Value { get; }

                public TagAttribute(object? value = null) { Value = value; }
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class AssistedAttribute : Attribute { }
        }
        """;

    private static readonly MetadataReference AttributeReference = MetadataReference.CreateFromImage(
        EmitAssembly(CreateBaseCompilation(source: Attributes, assemblyName: "InjectionContracts"))
    );

    internal static CSharpCompilation CreateInput(
        string source,
        string assemblyName = "Consumer",
        params MetadataReference[] additionalReferences
    )
    {
        return CreateBaseCompilation(source, assemblyName)
            .AddReferences(additionalReferences)
            .AddReferences(AttributeReference);
    }

    private static CSharpCompilation CreateBaseCompilation(string source, string assemblyName)
    {
        var input = CreateCompilation(source, assemblyName);
        var runtimeReferences = input.References.Where(reference =>
        {
            return StringComparer.OrdinalIgnoreCase.Equals(reference.Display, typeof(InjectAttribute).Assembly.Location);
        });

        return input.RemoveReferences(runtimeReferences);
    }
}
