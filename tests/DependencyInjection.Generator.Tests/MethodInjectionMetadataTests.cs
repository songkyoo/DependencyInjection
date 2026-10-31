using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class MethodInjectionMetadataTests
{
    private const string FirstMethod =
        """
        [Inject]
        internal void Initialize([Tag("first")] in T value, [Assisted] string name = "first")
        {
            Value = value;
            Name = name;
        }
        """;

    private const string UpdatedMethod =
        """
        [Inject]
        internal void Update([Tag("second")] in T value, [Assisted] string label = "second")
        {
            Value = value;
            Name = label;
        }
        """;

    private const string DuplicateMethods =
        """
        [Inject] private void First() { }

        [Inject] internal void Second() { }
        """;

    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string[] Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                {
                    registrar.RegisterInstance(instance: 1, tag: "first");
                    registrar.RegisterInstance(instance: 2, tag: "second");
                    registrar.RegisterInstance(instance: "a", tag: "first");
                    registrar.RegisterInstance(instance: "b", tag: "second");
                });
                var number = new Service<int>();
                var text = new Service<string>();

                container.For<Service<int>>().Inject(number);
                container.For<Service<string>>().Inject(text);

                return new[] { $"{number.Value}|{number.Name}", $"{text.Value}|{text.Name}" };
            }
        }
        """;

    [Test]
    public void ReplacedMetadata_ShouldRefreshClosedGenericMethodContractAndRestoreOriginal()
    {
        var firstImage = EmitService(FirstMethod);
        var secondImage = EmitService(UpdatedMethod);
        var firstReference = MetadataReference.CreateFromImage(firstImage);
        var secondReference = MetadataReference.CreateFromImage(secondImage);
        var input = CreateCompilation(Consumer, additionalReferences: firstReference);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);
        var originalSources = GetContextSources(driver);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(originalSources, Has.Count.EqualTo(2));
        Assert.That(Execute<string[]>(first, firstImage), Is.EqualTo(["1|first", "a|first"]));

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceReference(firstReference, secondReference),
            out var updated,
            out diagnostics
        );

        var updatedSources = GetContextSources(driver);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(updatedSources.Keys, Is.EquivalentTo(originalSources.Keys));
        Assert.That(updatedSources.Values, Is.All.Contains("string label = \"second\""));
        Assert.That(updatedSources.Values, Is.All.Contains(".Update("));
        Assert.That(Execute<string[]>(updated, secondImage), Is.EqualTo(["2|second", "b|second"]));

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(GetContextSources(driver), Is.EquivalentTo(originalSources));
        Assert.That(Execute<string[]>(restored, firstImage), Is.EqualTo(["1|first", "a|first"]));
    }

    [TestCase("[Inject] private void Initialize() { }", "MADI0018")]
    [TestCase(DuplicateMethods, "MADI0005")]
    [TestCase("[Inject] public void Initialize(ref T value) { }", "MADI0008")]
    public void InvalidReplacementMetadata_ShouldReportEachForUseAndRestoreAfterCorrection(
        string declaration,
        string expectedId
    )
    {
        var firstImage = EmitService(FirstMethod);
        var invalidImage = EmitService(declaration);
        var firstReference = MetadataReference.CreateFromImage(firstImage);
        var invalidReference = MetadataReference.CreateFromImage(invalidImage);
        var input = CreateCompilation(Consumer, additionalReferences: firstReference);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        var originalSources = GetContextSources(driver);

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceReference(firstReference, invalidReference),
            out _,
            out diagnostics
        );

        var useTexts = diagnostics.Select(diagnostic =>
        {
            var span = diagnostic.Location.SourceSpan;

            return Consumer.Substring(span.Start, span.Length);
        });

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId, expectedId]));
        Assert.That(useTexts, Is.EqualTo(["Service<int>", "Service<string>"]));
        Assert.That(diagnostics.SelectMany(diagnostic => diagnostic.AdditionalLocations), Is.Empty);
        Assert.That(GetContextSources(driver), Is.Empty);

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(GetContextSources(driver), Is.EquivalentTo(originalSources));
        Assert.That(Execute<string[]>(restored, firstImage), Is.EqualTo(["1|first", "a|first"]));
    }

    [Test]
    public void ReplacedMetadataConstructor_ShouldReplaceMethodPathWithoutChangingForUse()
    {
        const string consumer =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service<int>>(); }
            }
            """;
        var methodImage = EmitService(FirstMethod);
        var constructorImage = EmitService("[Inject] internal Service(T value) { Value = value; }");
        var methodReference = MetadataReference.CreateFromImage(methodImage);
        var constructorReference = MetadataReference.CreateFromImage(constructorImage);
        var input = CreateCompilation(consumer, additionalReferences: methodReference);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out _, out var diagnostics);
        var original = GetContextSources(driver).Single();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(original.Value, Does.Contain("void Inject("));

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceReference(methodReference, constructorReference),
            out var updated,
            out diagnostics
        );
        var creation = GetContextSources(driver).Single();
        var factories = driver.GetRunResult().Results.Single().GeneratedSources
            .Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(creation.Key, Is.EqualTo(original.Key));
        Assert.That(creation.Value, Does.Contain(" Create(").And.Not.Contain("void Inject("));
        Assert.That(factories, Does.Contain("typeof(global::Service<int>)"));
        AssertCompiles(updated);
    }

    [Test]
    public void ExternalBaseContext_ShouldUseBaseDefaultsAndVirtualDispatchWithoutInheritingInjectSelection()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                public string Value = "uninitialized";
                [Inject] public virtual void Initialize([Assisted] string name = "base") { Value = name; }
            }

            public class Derived : Base
            {
                public override void Initialize(string name = "derived") { Value = "override:" + name; }
            }

            public class Hidden : Base
            {
                public new void Initialize(string name = "hidden") { Value = "hidden:" + name; }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string[] Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var derived = container.For<Derived>().Create();
                    var hidden = container.For<Hidden>().Create();
                    var before = derived.Value;

                    container.For<Base>().Inject(derived);
                    container.For<Base>().Inject(hidden);

                    return new[] { before, derived.Value, hidden.Value };
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<string[]>(compilation, external), Is.EqualTo(["uninitialized", "override:base", "base"]));
    }

    private static byte[] EmitService(string declaration)
    {
        return EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]

            public class Service<T>
            {
                public T Value;
                public string Name;

                public Service() { }

                {{declaration}}
            }
            """,
            assemblyName: "ExternalServices"
        ));
    }

    private static Dictionary<string, string> GetContextSources(GeneratorDriver driver)
    {
        return driver
            .GetRunResult()
            .Results
            .Single()
            .GeneratedSources
            .Where(source => source.HintName.StartsWith("InjectionContextExtensions."))
            .ToDictionary(source => source.HintName, source => source.SourceText.ToString());
    }
}
