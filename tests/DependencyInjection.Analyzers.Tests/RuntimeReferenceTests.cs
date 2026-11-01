using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class RuntimeReferenceTests
{
    [TestCase("OtherRuntime")]
    [TestCase("Macaron.DependencyInjection.Extensions")]
    public void WithoutRuntimeReference_ShouldIgnoreMatchingTypesFromAnotherAssembly(string assemblyName)
    {
        var declarations = CreateCompilation(
            """
            namespace Macaron.DependencyInjection
            {
                public sealed class InjectAttribute : System.Attribute { }

                public sealed class AssistedAttribute : System.Attribute { }
            }
            """,
            assemblyName
        );
        var runtimeReferences = declarations
            .References
            .Where(reference =>
            {
                return declarations.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol
                {
                    Name: "Macaron.DependencyInjection",
                };
            })
            .ToArray();
        var image = EmitAssembly(declarations.RemoveReferences(runtimeReferences));
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject]
                public Service([Assisted] ref int value) { }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(image)
        ).RemoveReferences(runtimeReferences);
        var diagnostics = Analyze(input);

        Assert.That(diagnostics, Is.Empty);
        AssertCompiles(input);
    }
}
