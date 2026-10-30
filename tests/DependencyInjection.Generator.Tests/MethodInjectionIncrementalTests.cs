using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class MethodInjectionIncrementalTests
{
    private const string Method =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] int count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedTag =
        """
        [Inject]
        public void Initialize([Tag("second")] string dependency = "fallback", [Assisted] int count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedDependencyDefault =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "updated", [Assisted] int count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedAssistedDefault =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] int count = 2)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedMethodName =
        """
        [Inject]
        public void Update([Tag("first")] string dependency = "fallback", [Assisted] int count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedParameterName =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] int increment = 1)
        {
            Value = $"{dependency}|{increment}";
        }
        """;

    private const string ChangedParameterType =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] long count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedRefKind =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] in int count = 1)
        {
            Value = $"{dependency}|{count}";
        }
        """;

    private const string ChangedParams =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] params int[] count)
        {
            Value = $"{dependency}|{count.Length}";
        }
        """;

    private const string ChangedBody =
        """
        [Inject]
        public void Initialize([Tag("first")] string dependency = "fallback", [Assisted] int count = 1)
        {
            Value = $"{dependency}|{count}|body";
        }
        """;

    private const string Constructor = "[Inject] public Service() { }";
    private const string UnmarkedMethod = "public void Initialize() { }";
    private const string ParameterlessMethod = "[Inject] public void Initialize() { }";
    private const string DuplicateMethods =
        """
        [Inject] public void First() { }

        [Inject] public void Second() { }
        """;

    private const string ForUse =
        """
        using Macaron.DependencyInjection;

        public class Configuration
        {
            public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
        }
        """;

    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public class Stable { }

        public static class Probe
        {
            public static string Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                    registrar.RegisterSingleton(instance: "provided", tag: "second"));
                var service = new Service();

                container.For<Service>().Inject(service);
                container.For<Stable>().Create();

                return service.Value;
            }
        }
        """;

    [TestCase(UnmarkedMethod)]
    [TestCase(Constructor)]
    public void DirectMethodInjectAddedAndRemoved_ShouldReplaceEntryPointAndFactory(string constructorPath)
    {
        var original = CreateService(constructorPath);
        var input = CreateCompilation(ForUse).AddSyntaxTrees(original);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);
        var creation = GetContextSource(driver, "Service");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(creation.SourceText.ToString(), Does.Contain(" Create(").And.Not.Contain("void Inject("));
        Assert.That(GetFactories(driver), Does.Contain("typeof(global::Service)"));
        AssertCompiles(first);

        var method = CreateService(ParameterlessMethod);

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, method),
            out var output,
            out diagnostics
        );

        var injection = GetContextSource(driver, "Service");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(injection.HintName, Is.EqualTo(creation.HintName));
        Assert.That(injection.SourceText.ToString(), Does.Contain("void Inject(").And.Not.Contain(" Create("));
        Assert.That(GetFactories(driver), Does.Not.Contain("typeof(global::Service)"));
        AssertCompiles(output);

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            GetContextSource(driver, "Service").SourceText.ToString(),
            Is.EqualTo(creation.SourceText.ToString())
        );
        Assert.That(GetFactories(driver), Does.Contain("typeof(global::Service)"));
        AssertCompiles(restored);
    }

    [TestCase(DuplicateMethods, "MADI0005")]
    [TestCase("[Inject] private void Initialize() { }", "MADI0018")]
    [TestCase("[Inject] public void Initialize(ref int value) { }", "MADI0008")]
    public void InvalidDeclarationCorrected_ShouldRestoreInjectionWithoutChangingOtherTargets(
        string declaration,
        string expectedId
    )
    {
        var original = CreateService(Method);
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);
        var injection = GetContextSource(driver, "Service");
        var stable = GetContextSource(driver, "Stable");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("fallback|1"));

        var invalid = CreateService(declaration);

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, invalid), out _, out diagnostics);

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
        Assert.That(sources.Any(source => source.HintName == injection.HintName), Is.False);
        Assert.That(GetContextSource(driver, "Stable").SourceText.ToString(), Is.EqualTo(stable.SourceText.ToString()));

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            GetContextSource(driver, "Service").SourceText.ToString(),
            Is.EqualTo(injection.SourceText.ToString())
        );
        Assert.That(Execute<string>(restored), Is.EqualTo("fallback|1"));
    }

    [TestCase(ChangedTag, "provided|1", "int count = 1")]
    [TestCase(ChangedDependencyDefault, "updated|1", "int count = 1")]
    [TestCase(ChangedAssistedDefault, "fallback|2", "int count = 2")]
    [TestCase(ChangedMethodName, "fallback|1", ".Update(")]
    [TestCase(ChangedParameterName, "fallback|1", "int increment = 1")]
    [TestCase(ChangedParameterType, "fallback|1", "long count = 1")]
    [TestCase(ChangedRefKind, "fallback|1", "in int count = 1")]
    [TestCase(ChangedParams, "fallback|0", "params int[] count")]
    public void MethodContractChange_ShouldRefreshInjectionAndKeepOtherRenderingCached(
        string declaration,
        string expectedValue,
        string expectedSignature
    )
    {
        var original = CreateService(Method);
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true).RunGeneratorsAndAnalyze(
            input,
            out var first,
            out var diagnostics
        );
        var firstSource = GetContextSource(driver, "Service");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("fallback|1"));

        var updated = CreateService(declaration);

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        var source = GetContextSource(driver, "Service");
        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(diagnostics, Is.Empty);
        Assert.That(source.HintName, Is.EqualTo(firstSource.HintName));
        Assert.That(
            source.SourceText.ToString(),
            Does.Contain(expectedSignature).And.Not.EqualTo(firstSource.SourceText.ToString())
        );
        Assert.That(
            steps["InjectionSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(
            steps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            steps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<string>(output), Is.EqualTo(expectedValue));

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            GetContextSource(driver, "Service").SourceText.ToString(),
            Is.EqualTo(firstSource.SourceText.ToString())
        );
        Assert.That(Execute<string>(restored), Is.EqualTo("fallback|1"));
    }

    [Test]
    public void SelectedMethodBodyChange_ShouldReuseRenderingAndExecuteUpdatedBody()
    {
        var original = CreateService(Method);
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true).RunGeneratorsAndAnalyze(
            input,
            out _,
            out var diagnostics
        );

        Assert.That(diagnostics, Is.Empty);

        var updated = CreateService(ChangedBody);

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            steps["InjectionSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            steps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            steps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<string>(output), Is.EqualTo("fallback|1|body"));
    }

    [Test]
    public void DeclarationLocationChange_ShouldMoveDiagnosticAndReuseValidInjectionRendering()
    {
        const string invalidSource =
            """
            using Macaron.DependencyInjection;

            public class Invalid
            {
                [Inject] private void Initialize() { }
            }
            """;
        var invalid = CSharpSyntaxTree.ParseText(invalidSource, InputParseOptions, path: "Invalid.cs");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(CreateService(Method), invalid);
        var driver = CreateDriver(trackIncrementalSteps: true).RunGeneratorsAndAnalyze(
            input,
            out _,
            out var firstDiagnostics
        );

        Assert.That(firstDiagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));

        var moved = CSharpSyntaxTree.ParseText("\n\n" + invalidSource, InputParseOptions, path: "Invalid.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(invalid, moved),
            out var output,
            out var diagnostics
        );

        var diagnostic = diagnostics.Single();
        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0018"));
        Assert.That(diagnostic.Location.SourceTree, Is.SameAs(moved));
        Assert.That(
            diagnostic.Location.SourceSpan.Start,
            Is.EqualTo(firstDiagnostics.Single().Location.SourceSpan.Start + 2)
        );
        Assert.That(
            steps["InjectionSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<string>(output), Is.EqualTo("fallback|1"));
    }

    [Test]
    public void BaseInjectSelectionChange_ShouldKeepDerivedConstructorAndMethodOutputsCached()
    {
        const string firstBase =
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                [Inject] public void First() { }
                public void Second() { }
            }
            """;
        const string secondBase =
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                public void First() { }
                [Inject] public void Second() { }
            }
            """;
        var original = CSharpSyntaxTree.ParseText(firstBase, InputParseOptions, path: "Base.cs");
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Constructed : Base { }

            public class Injected : Base
            {
                public int Value;
                [Inject] public void Initialize([Assisted] int value) { Value = value; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var injected = new Injected();

                    container.For<Constructed>().Create();
                    container.For<Injected>().Inject(injected, 42);

                    return injected.Value;
                }
            }
            """
        ).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true).RunGeneratorsAndAnalyze(
            input,
            out _,
            out var diagnostics
        );

        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(secondBase, InputParseOptions, path: "Base.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            steps["InjectionSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            steps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            steps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(Execute<int>(output), Is.EqualTo(42));
    }

    private static SyntaxTree CreateService(string declaration)
    {
        return CSharpSyntaxTree.ParseText(
            text:
                $$"""
                using Macaron.DependencyInjection;

                public class Service
                {
                    public string Value;

                    {{declaration}}
                }
                """,
            options: InputParseOptions,
            path: "Service.cs"
        );
    }

    private static GeneratedSourceResult GetContextSource(GeneratorDriver driver, string typeName)
    {
        return driver
            .GetRunResult()
            .Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName.StartsWith($"InjectionContextExtensions.{typeName}_"));
    }

    private static string GetFactories(GeneratorDriver driver)
    {
        return driver
            .GetRunResult()
            .Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName.StartsWith("Factories."))
            .SourceText
            .ToString();
    }
}
