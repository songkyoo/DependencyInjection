using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationIncrementalTests
{
    private const string ServiceDeclaration = "public class Service { }";

    private const string ForUse =
        """
        using Macaron.DependencyInjection;

        public class CreationConfiguration
        {
            public void Configure(IDependencyResolver resolver)
            {
                resolver.For<Service>();
            }
        }
        """;

    [Test]
    public void RemovingLastFor_ShouldRemoveCreationAndUnregisteredFactory()
    {
        var use = CSharpSyntaxTree.ParseText(ForUse, InputParseOptions, "Creation.cs");
        var input = CreateCompilation(ServiceDeclaration).AddSyntaxTrees(use);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Length, Is.EqualTo(3));

        driver = driver.RunGeneratorsAndAnalyze(input.RemoveSyntaxTrees(use), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Not.Contain("typeof(global::Service)"));
        AssertCompiles(output);
    }

    [Test]
    public void RemovingLastFor_ShouldKeepFactoryUsedByRegistration()
    {
        const string registration =
            """
            using Macaron.DependencyInjection;

            public class RegistrationConfiguration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                }
            }
            """;
        var use = CSharpSyntaxTree.ParseText(ForUse, InputParseOptions, "Creation.cs");
        var input = CreateCompilation(ServiceDeclaration).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(registration, InputParseOptions, "Registration.cs"), use);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Length, Is.EqualTo(3));

        driver = driver.RunGeneratorsAndAnalyze(input.RemoveSyntaxTrees(use), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Service)"));
        AssertCompiles(output);
    }
}
