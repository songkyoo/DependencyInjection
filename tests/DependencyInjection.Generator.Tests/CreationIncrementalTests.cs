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

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(factories, Does.Not.Contain("typeof(global::Service)"));
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
            CSharpSyntaxTree.ParseText(registration, InputParseOptions, "Registration.cs"),
            use
        );
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Length, Is.EqualTo(3));

        driver = driver.RunGeneratorsAndAnalyze(input.RemoveSyntaxTrees(use), out var output, out diagnostics);

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(factories, Does.Contain("typeof(global::Service)"));
        AssertCompiles(output);
    }

    [Test]
    public void RemovingRepeatedMethodForUses_ShouldRemoveOnlyLastInjectionAndPreserveOtherConstructionPaths()
    {
        const string additionalUse =
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public class AdditionalConfiguration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service?>(); }
            }
            """;
        const string creationUse =
            """
            using Macaron.DependencyInjection;

            public class StableConfiguration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Stable>(); }
            }
            """;
        var firstUse = CSharpSyntaxTree.ParseText(ForUse, InputParseOptions, path: "First.cs");
        var secondUse = CSharpSyntaxTree.ParseText(additionalUse, InputParseOptions, path: "Second.cs");
        var creation = CSharpSyntaxTree.ParseText(creationUse, InputParseOptions, path: "Creation.cs");
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public void Initialize() { }
            }

            public class Stable { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Stable>());

                    return container.Resolve<Stable>() is Stable;
                }
            }
            """
        ).AddSyntaxTrees(firstUse, secondUse, creation);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        var injection = sources.Single(source => source.HintName.StartsWith("InjectionContextExtensions.Service_"));
        var stable = sources.Single(source => source.HintName.StartsWith("InjectionContextExtensions.Stable_"));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(2));

        var oneMethodUse = input.RemoveSyntaxTrees(firstUse);

        driver = driver.RunGeneratorsAndAnalyze(oneMethodUse, out _, out diagnostics);
        sources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            sources.Single(source => source.HintName == injection.HintName).SourceText.ToString(),
            Does.Contain("InjectionContext<global::Service?>")
        );

        var noMethodUses = oneMethodUse.RemoveSyntaxTrees(secondUse);

        driver = driver.RunGeneratorsAndAnalyze(noMethodUses, out var output, out diagnostics);
        sources = driver.GetRunResult().Results.Single().GeneratedSources;

        var contextSources = sources
            .Where(source => source.HintName.StartsWith("InjectionContextExtensions."))
            .ToArray();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(contextSources.Select(source => source.HintName), Is.EqualTo([stable.HintName]));
        Assert.That(contextSources.Single().SourceText.ToString(), Is.EqualTo(stable.SourceText.ToString()));
        Assert.That(Execute<bool>(output), Is.True);

        driver = driver.RunGeneratorsAndAnalyze(noMethodUses.RemoveSyntaxTrees(creation), out output, out diagnostics);
        sources = driver.GetRunResult().Results.Single().GeneratedSources;

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Any(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.False);
        Assert.That(factories, Does.Contain("typeof(global::Stable)").And.Not.Contain("typeof(global::Service)"));
        Assert.That(Execute<bool>(output), Is.True);
    }
}
