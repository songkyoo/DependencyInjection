using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class AssistedIncrementalTests
{
    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                    registrar.RegisterSingleton(instance: "resolved"));
                return container.For<Service>().Create().Value;
            }
        }
        """;

    private const string Declaration =
        """
        #nullable enable
        using Macaron.DependencyInjection;

        public class Service
        {
            public string Value { get; }
            [Inject] public Service([Assisted] string value = "first") { Value = value; }
        }
        """;

    private const string ChangedDefault =
        """
        #nullable enable
        using Macaron.DependencyInjection;

        public class Service
        {
            public string Value { get; }
            [Inject] public Service([Assisted] string value = "second") { Value = value; }
        }
        """;

    private const string RenamedParameter =
        """
        #nullable enable
        using Macaron.DependencyInjection;

        public class Service
        {
            public string Value { get; }
            [Inject] public Service([Assisted] string name = "first") { Value = name; }
        }
        """;

    private const string DiDeclaration =
        """
        using Macaron.DependencyInjection;

        public class Service
        {
            public string Value { get; }
            [Inject] public Service(string value = "first") { Value = value; }
        }
        """;

    [TestCase(ChangedDefault, "second", "value")]
    [TestCase(RenamedParameter, "first", "name")]
    public void AssistedSignatureChange_ShouldRerenderCreateAndKeepFactoryRenderingCached(
        string declaration,
        string expectedValue,
        string expectedName
    )
    {
        var original = CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("first"));

        var updated = CSharpSyntaxTree.ParseText(declaration, InputParseOptions, "Service.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);

        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(
            steps["CreationSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(
            steps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );

        var create = output
            .GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")!
            .GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.That(create.Parameters[1].Name, Is.EqualTo(expectedName));
        Assert.That(Execute<string>(output), Is.EqualTo(expectedValue));
    }

    [Test]
    public void UnrelatedSourceChange_ShouldReuseAssistedSignatureRendering()
    {
        var service = CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs");
        var other = CSharpSyntaxTree.ParseText("public class Other { public int Value => 1; }", InputParseOptions, "Other.cs");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(service, other);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(
            text: "public class Other { public int Value => 2; }",
            options: InputParseOptions,
            path: "Other.cs"
        );

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(other, updated),
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
    public void AssistedRemovalAndRestoration_ShouldRefreshRegistrationDiagnosticsAndFactoryTable()
    {
        const string registration =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterSingleton(new Service());
                    registrar.RegisterTransient<Service>();
                }
            }
            """;
        var original = CSharpSyntaxTree.ParseText(Declaration, InputParseOptions, "Service.cs");
        var input = CreateCompilation(Consumer)
            .AddSyntaxTrees(
                original,
                CSharpSyntaxTree.ParseText(registration, InputParseOptions, "Registration.cs")
            );
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0014"]));
        Assert.That(Execute<string>(first), Is.EqualTo("first"));

        var updated = CSharpSyntaxTree.ParseText(DiDeclaration, InputParseOptions, "Service.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(output), Is.EqualTo("resolved"));
        Assert.That(
            driver
                .GetRunResult()
                .Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName.StartsWith("Factories."))
                .SourceText
                .ToString(),
            Does.Contain("typeof(global::Service)")
        );

        driver = driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0014"]));
        Assert.That(Execute<string>(restored), Is.EqualTo("first"));
        Assert.That(
            driver
                .GetRunResult()
                .Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName.StartsWith("Factories."))
                .SourceText
                .ToString(),
            Does.Not.Contain("typeof(global::Service)")
        );
    }

    [Test]
    public void ExternalDefaultChange_ShouldRefreshGeneratedAssistedSignature()
    {
        var firstImage = EmitAssembly(CreateCompilation(Declaration, "ExternalServices"));
        var secondImage = EmitAssembly(CreateCompilation(ChangedDefault, "ExternalServices"));
        var firstReference = MetadataReference.CreateFromImage(firstImage);
        var secondReference = MetadataReference.CreateFromImage(secondImage);
        var input = CreateCompilation(Consumer, additionalReferences: firstReference);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(
            input,
            out var first,
            out var diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first, firstImage), Is.EqualTo("first"));

        driver.RunGeneratorsAndAnalyze(
            input.ReplaceReference(firstReference, secondReference),
            out var updated,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(updated, secondImage), Is.EqualTo("second"));
    }
}
