using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationIncrementalTests
{
    private const string Source =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string Run()
            {
                using var container = DependencyContainerFactory.Create(r =>
                {
                    r.RegisterSingleton(instance: "first", tag: "first");
                    r.RegisterSingleton(instance: "second", tag: "second");
                });

                return container.For<Service>().Create().Value;
            }
        }
        """;

    private const string Declaration =
        """
        using Macaron.DependencyInjection;
        public class Service
        {
            public string Value;

            [Inject] public Service([Tag(Tags.Value)] in string value) { Value = value; }
        }

        public static class Tags
        {
            public const string Value = "first";
        }
        """;

    [TestCase("public class Unrelated { public int Value() => 1; }", "public class Unrelated { public int Value() => 2; }")]
    [TestCase("", "\n\n")]
    public void UnrelatedChanges_ShouldReuseCreationRendering(string original, string updated)
    {
        var input = CreateCompilation(Source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs"));
        var unrelated = CSharpSyntaxTree.ParseText(original, InputParseOptions, "Other.cs");

        input = input.AddSyntaxTrees(unrelated);

        var driver = CreateDriver(true).RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(unrelated, CSharpSyntaxTree.ParseText(updated, InputParseOptions, "Other.cs")),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<string>(output), Is.EqualTo("first"));
    }

    [Test]
    public void DiagnosticLocationChange_ShouldUpdateLocationAndReuseCreationRendering()
    {
        var source = (Source + "public abstract class Invalid { }")
            .Replace("return container.For<Service>()", "container.For<Invalid>(); return container.For<Service>()");
        var declaration = CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs");
        var input = CreateCompilation(source).AddSyntaxTrees(declaration);
        var driver = CreateDriver(true).RunGeneratorsAndUpdateCompilation(input, out _, out var firstDiagnostics);

        Assert.That(firstDiagnostics.Single().Id, Is.EqualTo("MADI0003"));

        var updated = CreateCompilation("\n\n" + source).AddSyntaxTrees(declaration);

        driver = driver.RunGeneratorsAndUpdateCompilation(updated, out var output, out var diagnostics);

        Assert.That(diagnostics.Single().Location.SourceSpan.Start, Is.EqualTo(firstDiagnostics.Single().Location.SourceSpan.Start + 2));
        Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(updated.SyntaxTrees.First()));
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<string>(output), Is.EqualTo("first"));
    }

    [TestCase("tag")]
    [TestCase("constructor")]
    [TestCase("accessibility")]
    [TestCase("array")]
    [TestCase("inject")]
    [TestCase("ref")]
    public void DeclarationChange_ShouldRefreshCreationOrRemoveAndRestoreIt(string change)
    {
        var declaration = CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs");
        var input = CreateCompilation(Source).AddSyntaxTrees(declaration);
        var driver = CreateDriver(true).RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("first"));

        var updated = change switch
        {
            "tag" => Declaration.Replace("Value = \"first\"", "Value = \"second\""),
            "constructor" => Declaration.Replace("in string value", "string value"),
            "accessibility" => Declaration.Replace("public Service(", "private Service("),
            "array" => Declaration.Replace("Tag(Tags.Value)", "Tag(new[] { 1 })"),
            "inject" => Declaration.Replace("[Inject]", ""),
            "ref" => Declaration.Replace("in string value", "ref string value"),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        var valid = change is "tag" or "constructor";

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(declaration, CSharpSyntaxTree.ParseText(updated, InputParseOptions, "Service.cs")),
            out var output,
            out diagnostics
        );

        var result = driver.GetRunResult().Results.Single();

        Assert.That(diagnostics.Length, Is.EqualTo(valid ? 0 : 1));
        Assert.That(result.GeneratedSources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(valid ? 1 : 0));

        if (valid)
        {
            Assert.That(result.TrackedSteps["CreationSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Modified));
            Assert.That(Execute<string>(output), Is.EqualTo(change == "tag" ? "second" : "first"));
        }

        driver.RunGeneratorsAndUpdateCompilation(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(restored), Is.EqualTo("first"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemovingLastFor_ShouldRemoveCreationAndRetainOnlyRegisteredFactory(bool registered)
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Service { }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    resolver.For<Service>();
                }
            }
            """;
        var original = registered ? source : source.Replace("registrar.RegisterTransient<Service>();", "");
        var input = CreateCompilation(original);
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(original.Replace("resolver.For<Service>();", "")), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString().Contains("typeof(global::Service)"), Is.EqualTo(registered));
        AssertCompiles(output);
    }
}
