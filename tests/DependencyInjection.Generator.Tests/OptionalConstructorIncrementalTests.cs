using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class OptionalConstructorIncrementalTests
{
    private const string Consumer =
        """
        using System;
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string[] Run()
            {
                using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service>());

                return new[]
                {
                    Read(() => container.Resolve<Service>()),
                    Read(() => container.For<Service>().Create()),
                };

                string Read(Func<Service> factory)
                {
                    try { return factory().Value; }
                    catch (InvalidOperationException) { return "missing"; }
                }
            }
        }
        """;

    [TestCase("string value", "missing", "string? value = null", "<null>")]
    [TestCase("string? value = null", "<null>", "string value", "missing")]
    [TestCase("string value", "missing", "string value = \"fallback\"", "fallback")]
    [TestCase("string value = \"fallback\"", "fallback", "string value", "missing")]
    [TestCase("string? value = null", "<null>", "string? value = \"guest\"", "guest")]
    [TestCase("string value = \"first\"", "first", "string value = \"second\"", "second")]
    public void ParameterContractChange_ShouldRefreshBothConstructionPathsAndRestoreOriginal(
        string originalParameter,
        string originalValue,
        string updatedParameter,
        string updatedValue
    )
    {
        var original = ParseService(originalParameter);
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(first), Is.EqualTo([originalValue, originalValue]));

        var updated = ParseService(updatedParameter);

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(output), Is.EqualTo([updatedValue, updatedValue]));
        AssertRenderingModified(driver);

        driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(restored), Is.EqualTo([originalValue, originalValue]));
    }

    [Test]
    public void NullableAnnotationOnlyChange_ShouldKeepRequiredResolveAndReuseRendering()
    {
        var original = ParseService("string value");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var updated = ParseService("string? value");

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(output), Is.EqualTo(["missing", "missing"]));

        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(steps["FactoriesSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Cached));
        Assert.That(steps["CreationSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Cached));
    }

    [Test]
    public void ExternalDefaultChange_ShouldRefreshGeneratedDefaultsWithoutChangingConsumer()
    {
        var firstImage = EmitAssembly(CreateCompilation(DeclareService("string? value = null"), "ExternalServices"));
        var firstReference = MetadataReference.CreateFromImage(firstImage);
        var input = CreateCompilation(Consumer, additionalReferences: firstReference);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(first, firstImage), Is.EqualTo(["<null>", "<null>"]));

        var updatedImage = EmitAssembly(CreateCompilation(
            source: DeclareService("string? value = \"external\""),
            assemblyName: "ExternalServices"
        ));
        var updatedReference = MetadataReference.CreateFromImage(updatedImage);

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceReference(firstReference, updatedReference),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(output, updatedImage), Is.EqualTo(["external", "external"]));
        AssertRenderingModified(driver);
    }

    private static SyntaxTree ParseService(string parameter)
    {
        return CSharpSyntaxTree.ParseText(DeclareService(parameter), InputParseOptions, "Service.cs");
    }

    private static string DeclareService(string parameter)
    {
        return $$"""
            #nullable enable
            public sealed class Service
            {
                public string Value { get; }

                public Service({{parameter}})
                {
                    Value = value is null ? "<null>" : value;
                }
            }
            """;
    }

    private static void AssertRenderingModified(GeneratorDriver driver)
    {
        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(steps["FactoriesSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Modified));
        Assert.That(steps["CreationSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Modified));
    }
}
