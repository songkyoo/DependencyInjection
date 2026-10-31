using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class TagValueAccessibilityTests
{
    private const string ReferencedTagConsumer =
        """
        using Macaron.DependencyInjection;

        public static class Configuration
        {
            public static void Configure(IDependencyRegistrar registrar) => registrar.RegisterTransient<Service>();
        }
        """;

    [Test]
    public void TagTypeAccessibilityChange_ShouldUpdateDiagnosticAndRestoreFactory()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public sealed partial class Service
            {
                public string Value { get; }

                [Inject]
                public Service([Tag(Kind.Main)] string value) { Value = value; }

                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterInstance(instance: "resolved", tag: Kind.Main);
                    registrar.RegisterTransient<Service>();
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(Service.Configure);

                    return container.Resolve<Service>().Value;
                }
            }
            """
        );
        var inaccessible = CSharpSyntaxTree.ParseText(
            text: "public sealed partial class Service { private enum Kind { Main } }",
            options: InputParseOptions,
            path: "Tag.cs"
        );

        input = input.AddSyntaxTrees(inaccessible);

        var driver = CreateDriver().RunGeneratorsAndAnalyze(
            input,
            out var withoutFactory,
            out var diagnostics
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0012"]));
        AssertCompiles(withoutFactory);

        var accessible = CSharpSyntaxTree.ParseText(
            text: "public sealed partial class Service { internal enum Kind { Main } }",
            options: InputParseOptions,
            path: "Tag.cs"
        );

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(inaccessible, accessible),
            out var restored,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(restored), Is.EqualTo("resolved"));

        driver.RunGeneratorsAndAnalyze(input, out withoutFactory, out diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0012"]));
        AssertCompiles(withoutFactory);
    }

    [Test]
    public void InaccessibleValueMissingInjectAndRef_ShouldReportIndependentErrors()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                private enum Kind { Main }

                public Service([Tag(Kind.Main)] ref int value) { }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(["MADI0012", "MADI0011", "MADI0008"]));
        AssertCompiles(compilation);
    }

    [TestCase("Kind.Main")]
    [TestCase("typeof(Hidden)")]
    [TestCase("typeof(Hidden[])")]
    [TestCase("typeof(System.Collections.Generic.List<Hidden>)")]
    [TestCase("typeof(System.Collections.Generic.List<Hidden[]>)")]
    [TestCase("typeof(Generic<>)")]
    [TestCase("typeof(Outer.Nested)")]
    public void UnusedInaccessibleTagType_ShouldReportAtValue(string expression)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                private enum Kind { Main }
                private class Hidden { }
                private class Generic<T> { }
                private class Outer
                {
                    public class Nested { }
                }

                [Inject]
                public Service([Tag(value: {{expression}})] string value) { }
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0012"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo(expression));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Tag").And.Contain("value").And.Contain("Service").And.EndsWith("."));
        AssertCompiles(compilation);
    }

    [TestCase("Kind.Main")]
    [TestCase("typeof(Hidden)")]
    public void RepeatedUses_ShouldReportDeclarationOnceAndPreserveValidFactory(string expression)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                private enum Kind { Main }
                private class Hidden { }

                [Inject]
                public Service([Tag({{expression}})] string value) { }
            }

            public class Valid { }

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterSingleton<object, Service>();
                    resolver.For<Service>();
                    resolver.For<Service>();
                    registrar.RegisterTransient<Valid>();
                    resolver.For<Valid>().Create();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0012"]));

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("typeof(global::Service)").And.Contain("typeof(global::Valid)"));
        AssertCompiles(compilation);
    }

    [TestCase("Kind.Main")]
    [TestCase("typeof(Hidden)")]
    [TestCase("typeof(System.Collections.Generic.List<Hidden>)")]
    [TestCase("typeof(System.Collections.Generic.List<>)")]
    [TestCase("PrivateTag")]
    [TestCase("nameof(PrivateHidden)")]
    [TestCase("(int)PrivateKind.Main")]
    [TestCase("(PrivateHidden[])null")]
    public void AccessibleValue_ShouldIgnoreAccessibilityOfOriginalExpression(string expression)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                internal enum Kind { Main }
                internal class Hidden { }
                private enum PrivateKind { Main }
                private class PrivateHidden { }
                private const string PrivateTag = "tagged";

                public string Value { get; }

                [Inject]
                public Service([Tag({{expression}})] string value) { Value = value; }

                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterInstance(instance: "resolved", tag: {{expression}});
                    registrar.RegisterTransient<Service>();
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(Service.Configure);

                    return container.Resolve<Service>().Value == "resolved";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("private", "Kind.Main", false)]
    [TestCase("private", "typeof(Hidden)", false)]
    [TestCase("private", "typeof(Hidden[])", false)]
    [TestCase("internal", "Kind.Main", false)]
    [TestCase("internal", "typeof(System.Collections.Generic.List<Hidden>)", false)]
    [TestCase("private", "Kind.Main", true)]
    public void ReferencedInaccessibleTagType_ShouldDiagnoseAndOmitFactory(
        string accessibility,
        string expression,
        bool friendAssembly
    )
    {
        var reference = CreateReferencedTagAssembly(accessibility, expression, friendAssembly);
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            ReferencedTagConsumer,
            assemblyName: "TagAccessibilityConsumer",
            additionalReferences: reference
        );
        var diagnostic = diagnostics.Single();
        var span = diagnostic.Location.SourceSpan;
        var locationText = ReferencedTagConsumer.Substring(span.Start, span.Length);
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0012"));
        Assert.That(locationText, Is.EqualTo("Service"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Service").And.Contain("value").And.Contain("Tag"));
        Assert.That(factories, Does.Not.Contain("typeof(global::Service)"));
        AssertCompiles(compilation);
    }

    [TestCase("internal", "Kind.Main", true)]
    [TestCase("internal", "typeof(Hidden)", true)]
    [TestCase("public", "typeof(Hidden)", false)]
    public void ReferencedAccessibleTagType_ShouldGenerateFactory(
        string accessibility,
        string expression,
        bool friendAssembly
    )
    {
        var reference = CreateReferencedTagAssembly(accessibility, expression, friendAssembly);
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            ReferencedTagConsumer,
            assemblyName: "TagAccessibilityConsumer",
            additionalReferences: reference
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factories, Does.Contain("typeof(global::Service)"));
        AssertCompiles(compilation);
    }

    private static MetadataReference CreateReferencedTagAssembly(
        string accessibility,
        string expression,
        bool friendAssembly
    )
    {
        var friendDeclaration = friendAssembly
            ? """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TagAccessibilityConsumer")]"""
            : "";
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            {{friendDeclaration}}

            public class Service
            {
                {{accessibility}} enum Kind { Main }
                {{accessibility}} class Hidden { }

                [Inject]
                public Service([Tag({{expression}})] string value) { }
            }
            """,
            assemblyName: "ExternalServices"
        ));

        return MetadataReference.CreateFromImage(external);
    }
}
