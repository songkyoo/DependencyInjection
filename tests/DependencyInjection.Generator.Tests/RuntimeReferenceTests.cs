using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class RuntimeReferenceTests
{
    [TestCase("Consumer")]
    [TestCase("Macaron.DependencyInjection")]
    public void WithoutRuntimeReference_ShouldGenerateNoSources(string assemblyName)
    {
        var input = CreateCompilation("public class Service { }", assemblyName);
        var runtimeReferences = input
            .References
            .Where(reference =>
            {
                return input.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol
                {
                    Name: "Macaron.DependencyInjection",
                };
            })
            .ToArray();

        input = input.RemoveReferences(runtimeReferences);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources, Is.Empty);
        AssertCompiles(output);
    }

    [Test]
    public void RemovingAndRestoringRuntimeReference_ShouldRemoveAndRestoreGeneratedSources()
    {
        var input = CreateCompilation("public class Service { }");
        var runtimeReferences = input
            .References.Where(reference =>
            {
                return input.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol
                {
                    Name: "Macaron.DependencyInjection",
                };
            })
            .ToArray();
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources, Has.Length.EqualTo(2));
        AssertCompiles(first);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.RemoveReferences(runtimeReferences),
            out var withoutRuntime,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources, Is.Empty);
        AssertCompiles(withoutRuntime);

        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources, Has.Length.EqualTo(2));
        AssertCompiles(restored);
    }
}
