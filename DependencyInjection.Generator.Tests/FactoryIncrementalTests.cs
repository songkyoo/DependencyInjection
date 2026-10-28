using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryIncrementalTests
{
    private const string Types =
        """
        public class Service { }
        public class Extra { }
        """;

    private const string SingleRegistration =
        """
        using Macaron.DependencyInjection;

        public class Configuration
        {
            public void Configure(IDependencyRegistrar registrar)
            {
                registrar.RegisterTransient<Service>();
            }
        }
        """;

    private const string WithExtraRegistration =
        """
        using Macaron.DependencyInjection;

        public class Configuration
        {
            public void Configure(IDependencyRegistrar registrar)
            {
                registrar.RegisterTransient<Service>();
                registrar.RegisterTransient<object, Extra>();
            }
        }
        """;

    [Test]
    public void AddingRegistration_ShouldAddFactoryToExistingTable()
    {
        var original = CSharpSyntaxTree.ParseText(SingleRegistration, InputParseOptions, "Configuration.cs");
        var input = CreateCompilation(Types).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(WithExtraRegistration, InputParseOptions, "Configuration.cs");
        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        var result = driver.GetRunResult().Results.Single();
        Assert.That(result.TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Modified));
        Assert.That(result.GeneratedSources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Service)").And.Contain("typeof(global::Extra)"));
        AssertCompiles(output);
    }

    [Test]
    public void RemovingRegistration_ShouldKeepRemainingFactory()
    {
        var original = CSharpSyntaxTree.ParseText(WithExtraRegistration, InputParseOptions, "Configuration.cs");
        var input = CreateCompilation(Types).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(SingleRegistration, InputParseOptions, "Configuration.cs");
        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        var result = driver.GetRunResult().Results.Single();
        Assert.That(result.TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason, Is.EqualTo(IncrementalStepRunReason.Modified));
        Assert.That(result.GeneratedSources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Service)").And.Not.Contain("typeof(global::Extra)"));
        AssertCompiles(output);
    }

    [Test]
    public void RemovingLastRegistrations_ShouldRemoveSharedFactoryFromCollectedOutput()
    {
        const string consumer =
            """
            public class Service { }

            public static class Probe
            {
                public static bool Run()
                {
                    try { return Macaron.DependencyInjection.Generated.Factories.Get<Service>() != null; }
                    catch (System.Collections.Generic.KeyNotFoundException) { return false; }
                }
            }
            """;
        const string registrations =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterTransient<object, Service>();
                }
            }
            """;
        var uses = CSharpSyntaxTree.ParseText(registrations, InputParseOptions, "Configuration.cs");
        var input = CreateCompilation(consumer).AddSyntaxTrees(uses);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(first), Is.True);

        driver.RunGeneratorsAndAnalyze(input.RemoveSyntaxTrees(uses), out var updated, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(updated), Is.False);
    }
}
