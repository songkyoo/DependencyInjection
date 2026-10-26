using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class TagValueAccessibilityTests
{
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
                    registrar.RegisterSingleton(instance: "resolved", tag: Kind.Main);
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

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            input,
            out var withoutFactory,
            out var diagnostics
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0013"]));
        AssertCompiles(withoutFactory);

        var accessible = CSharpSyntaxTree.ParseText(
            text: "public sealed partial class Service { internal enum Kind { Main } }",
            options: InputParseOptions,
            path: "Tag.cs"
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input.ReplaceSyntaxTree(inaccessible, accessible),
            out var restored,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(restored), Is.EqualTo("resolved"));

        driver.RunGeneratorsAndUpdateCompilation(input, out withoutFactory, out diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0013"]));
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

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(["MADI0013", "MADI0012", "MADI0009"]));
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
                private class Outer { public class Nested { } }

                [Inject]
                public Service([Tag(value: {{expression}})] string value) { }
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0013"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo(expression));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Tag").And.Contain("value").And.Contain("Service").And.EndsWith("."));
        AssertCompiles(compilation);
    }

    [TestCase("Kind.Main")]
    [TestCase("typeof(Hidden)")]
    public void RepeatedRegistration_ShouldReportDeclarationOnceAndPreserveValidFactory(string expression)
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
                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterSingleton<object, Service>();
                    registrar.RegisterTransient<Valid>();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0013"]));

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
                    registrar.RegisterSingleton(instance: "resolved", tag: {{expression}});
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

    [TestCase("private", "Kind.Main", false, false)]
    [TestCase("private", "typeof(Hidden)", false, false)]
    [TestCase("private", "typeof(Hidden[])", false, false)]
    [TestCase("internal", "Kind.Main", false, false)]
    [TestCase("internal", "typeof(System.Collections.Generic.List<Hidden>)", false, false)]
    [TestCase("internal", "Kind.Main", true, true)]
    [TestCase("internal", "typeof(Hidden)", true, true)]
    [TestCase("private", "Kind.Main", true, false)]
    [TestCase("public", "typeof(Hidden)", false, true)]
    public void ReferencedTagType_ShouldHonorAccessibilityAndFriendAssembly(
        string accessibility,
        string expression,
        bool friendAssembly,
        bool expectedFactory
    )
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;
            {{(friendAssembly ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TagAccessibilityConsumer\")]" : "")}}

            public class Service
            {
                {{accessibility}} enum Kind { Main }
                {{accessibility}} class Hidden { }

                [Inject]
                public Service([Tag({{expression}})] string value) { }
            }
            """, assemblyName: "ExternalServices"
        ));
        const string source =
            """
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar) => registrar.RegisterTransient<Service>();
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            source,
            assemblyName: "TagAccessibilityConsumer",
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(expectedFactory ? Array.Empty<string>() : ["MADI0013"]));

        if (!expectedFactory)
        {
            var diagnostic = diagnostics.Single();

            Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo("Service"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Service").And.Contain("value").And.Contain("Tag"));
        }

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories.Contains("typeof(global::Service)"), Is.EqualTo(expectedFactory));
        AssertCompiles(compilation);
    }
}
