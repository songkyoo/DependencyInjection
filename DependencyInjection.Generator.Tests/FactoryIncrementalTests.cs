using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryIncrementalTests
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

    [TestCase("array", "MADI0011")]
    [TestCase("inject", "MADI0012")]
    [TestCase("ref", "MADI0009")]
    [TestCase("dynamic", "MADI0002")]
    public void InvalidTagDeclarationChange_ShouldUpdateDiagnosticAndFactory(string invalidation, string expectedId)
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "first", tag: "first");
                        registrar.RegisterTransient<Service>();
                    });

                    return container.Resolve<Service>().Value;
                }
            }
            """
        );
        const string declaration =
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                [Inject]
                public Service([Tag("first")] string value) { Value = value; }
            }
            """;
        var valid = CSharpSyntaxTree.ParseText(declaration, InputParseOptions, "Service.cs");

        input = input.AddSyntaxTrees(valid);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("first"));

        var invalidDeclaration = invalidation switch
        {
            "array" => declaration.Replace("Tag(\"first\")", "Tag(new[] { 1 })"),
            "inject" => declaration.Replace("[Inject]", ""),
            "ref" => declaration.Replace("string value)", "ref string value)"),
            "dynamic" => declaration.Replace("string value)", "dynamic value)"),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidation)),
        };
        var invalid = CSharpSyntaxTree.ParseText(invalidDeclaration, InputParseOptions, "Service.cs");
        var invalidInput = input.ReplaceSyntaxTree(valid, invalid);

        driver = driver.RunGeneratorsAndUpdateCompilation(invalidInput, out var withoutFactory, out diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
        Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(invalid));
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
        AssertCompiles(withoutFactory);

        driver.RunGeneratorsAndUpdateCompilation(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(restored), Is.EqualTo("first"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TagConstantChange_ShouldRenderUpdatedFactoryWhenRegistrationIsUnchanged(bool inParameter)
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                [Inject]
                public Service([Tag(Tags.Value)] string value) { Value = value; }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "first", tag: "first");
                        registrar.RegisterSingleton(instance: "second", tag: "second");
                        registrar.RegisterTransient<Service>();
                    });

                    return container.Resolve<Service>().Value;
                }
            }
            """;
        var original = CSharpSyntaxTree.ParseText(
            text: "public static class Tags { public const string Value = \"first\"; }",
            options: InputParseOptions,
            path: "Tags.cs"
        );
        var parameterSource = inParameter ? source.Replace("string value)", "in string value)") : source;
        var input = CreateCompilation(parameterSource).AddSyntaxTrees(original);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("first"));

        var updated = CSharpSyntaxTree.ParseText(
            text: "public static class Tags { public const string Value = \"second\"; }",
            options: InputParseOptions,
            path: "Tags.cs"
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        Assert.That(Execute<string>(output), Is.EqualTo("second"));
    }

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

    [TestCase("public Service() { Value = \"original\"; }", "original", false)]
    [TestCase("public Service(int value) { Value = value.ToString(); }", "42", false)]
    [TestCase("public Service() { Value = \"original\"; }", "original", true)]
    [TestCase("public Service(int value) { Value = value.ToString(); }", "42", true)]
    public void ConstructorChange_ShouldRenderUpdatedFactoryWhenRegistrationSyntaxIsUnchanged(
        string originalConstructor,
        string originalValue,
        bool singleType
    )
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: 42);
                        registrar.RegisterSingleton(instance: "updated");
                        registrar.RegisterTransient<Service, Service>();
                    });

                    return container.Resolve<Service>().Value;
                }
            }
            """.Replace("RegisterTransient<Service, Service>", singleType ? "RegisterTransient<Service>" : "RegisterTransient<Service, Service>")
        );
        var originalType = CSharpSyntaxTree.ParseText(
            text: $"public sealed class Service {{ public string Value {{ get; }} {originalConstructor} }}",
            options: InputParseOptions,
            path: "Service.cs"
        );

        input = input.AddSyntaxTrees(originalType);

        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo(originalValue));

        var updatedType = CSharpSyntaxTree.ParseText(
            text: "public sealed class Service { public string Value { get; } public Service(string value) { Value = value; } }",
            options: InputParseOptions,
            path: "Service.cs"
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(originalType, updatedType),
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
        Assert.That(Execute<string>(output), Is.EqualTo("updated"));
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

    [TestCase(false)]
    [TestCase(true)]
    public void RemovingLastRegistration_ShouldRemoveFactoryFromCollectedOutput(bool singleType)
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public sealed class Service { }

            public static class Probe
            {
                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service, Service>();
                }

                public static bool Run()
                {
                    try { return Macaron.DependencyInjection.Generated.Factories.Get<Service>() != null; }
                    catch (System.Collections.Generic.KeyNotFoundException) { return false; }
                }
            }
            """;
        var registrationSource = singleType ? source.Replace("RegisterTransient<Service, Service>", "RegisterTransient<Service>") : source;
        var input = CreateCompilation(registrationSource);
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(first), Is.True);

        var updatedSource = registrationSource.Replace(singleType ? "registrar.RegisterTransient<Service>();" : "registrar.RegisterTransient<Service, Service>();", "");
        var updatedInput = CreateCompilation(updatedSource);

        driver.RunGeneratorsAndUpdateCompilation(updatedInput, out var updated, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(updated), Is.False);
    }

    [Test]
    public void ConstructorChange_ShouldRemoveDiagnosticAndGenerateFactory()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<object, Service>();
                }
            }
            """
        );
        var invalid = CSharpSyntaxTree.ParseText("public class Service { private Service() { } }", InputParseOptions, "Service.cs");

        input = input.AddSyntaxTrees(invalid);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var firstDiagnostics);

        Assert.That(firstDiagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0006"]));

        var valid = CSharpSyntaxTree.ParseText("public class Service { }", InputParseOptions, "Service.cs");

        driver = driver.RunGeneratorsAndUpdateCompilation(input.ReplaceSyntaxTree(invalid, valid), out var output, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        AssertCompiles(output);
        Assert.That(
            driver
                .GetRunResult()
                .Results
                .Single()
                .GeneratedSources
                .Single(result => result.HintName.StartsWith("Factories."))
                .SourceText
                .ToString(),
            Does.Contain("typeof(global::Service)")
        );
    }
}
