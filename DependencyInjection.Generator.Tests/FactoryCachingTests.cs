using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryCachingTests
{
    private const string RegistrationSource =
        """
        using Macaron.DependencyInjection;

        public class Service { }

        public class Configuration
        {
            public void Configure(IDependencyRegistrar registrar)
            {
                registrar.RegisterTransient<object, Service>();
            }
        }
        """;

    [Test]
    public void UnrelatedBodyChange_ShouldReuseFactoryRenderingAfterAnalysis()
    {
        var unrelated = CSharpSyntaxTree.ParseText(
            text: "public class Unrelated { public int Value() => 1; }",
            options: InputParseOptions,
            path: "Unrelated.cs"
        );
        var input = CreateCompilation(RegistrationSource).AddSyntaxTrees(unrelated);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(
            text: "public class Unrelated { public int Value() => 2; }",
            options: InputParseOptions,
            path: "Unrelated.cs"
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(unrelated, updated),
            out var output,
            out diagnostics
        );

        var result = driver.GetRunResult().Results.Single();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            result.TrackedSteps["FactoryGeneration"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(
            result.TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        AssertCompiles(output);
    }

    [Test]
    public void DiagnosticLocationChange_ShouldUpdateDiagnosticAndReuseFactoryRendering()
    {
        var source = RegistrationSource
            .Replace(
                "public class Service { }",
                """
                public class Service { }
                public class Invalid { private Invalid() { } }
                """
            )
            .Replace(
                "registrar.RegisterTransient<object, Service>();",
                """
                registrar.RegisterTransient<object, Service>();
                registrar.RegisterTransient<object, Invalid>();
                """
            );
        var input = CreateCompilation(source);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out _, out var firstDiagnostics);
        var originalDiagnostic = firstDiagnostics.Single();
        var updatedInput = CreateCompilation("\n\n" + source);

        driver = driver.RunGeneratorsAndUpdateCompilation(updatedInput, out var output, out var diagnostics);

        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0006"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(originalDiagnostic.Location.SourceSpan.Start + 2));
        Assert.That(diagnostic.Location.SourceTree, Is.SameAs(updatedInput.SyntaxTrees.Single()));
        Assert.That(
            diagnostic.AdditionalLocations.Single().SourceSpan.Start,
            Is.EqualTo(originalDiagnostic.AdditionalLocations.Single().SourceSpan.Start + 2)
        );
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        AssertCompiles(output);
    }

    [Test]
    public void ConstructorParameterChange_ShouldRenderUpdatedFactory()
    {
        var source = RegistrationSource.Replace(
            "public class Service { }",
            "public class Service { public Service(int value) { } }"
        );
        var input = CreateCompilation(source);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            CreateCompilation(source.Replace("int value", "string value")),
            out var output,
            out diagnostics
        );

        var result = driver.GetRunResult().Results.Single();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            result.TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(
            result.GeneratedSources.Single(generated => generated.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("resolver.Resolve<string>()")
        );
        AssertCompiles(output);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RegistrationSetChange_ShouldRenderUpdatedFactoryTable(bool addRegistration)
    {
        var withoutExtra = RegistrationSource + "\npublic class Extra { }";
        var withExtra = withoutExtra.Replace(
            "registrar.RegisterTransient<object, Service>();",
            """
            registrar.RegisterTransient<object, Service>();
            registrar.RegisterTransient<Extra, Extra>();
            """
        );
        var input = CreateCompilation(addRegistration ? withoutExtra : withExtra);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            CreateCompilation(addRegistration ? withExtra : withoutExtra),
            out var output,
            out diagnostics
        );

        var result = driver.GetRunResult().Results.Single();
        var source = result
            .GeneratedSources
            .Single(generated => generated.HintName.StartsWith("Factories."))
            .SourceText
            .ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            result.TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(source.Contains("typeof(global::Extra)"), Is.EqualTo(addRegistration));
        AssertCompiles(output);
    }
}
