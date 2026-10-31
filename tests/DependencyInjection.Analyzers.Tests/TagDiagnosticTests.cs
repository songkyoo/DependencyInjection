using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class TagDiagnosticTests
{
    private const string UnmarkedTaggedAndDefaultConstructors =
        """
        public Service([Tag("tagged")] string value) { }

        public Service() { }
        """;

    private const string UnmarkedInternalTaggedAndMarkedDefaultConstructors =
        """
        internal Service([Tag("tagged")] string value) { }

        [Inject] public Service() { }
        """;

    [TestCase("public Service([Tag] string value) { }")]
    [TestCase("internal Service([Tag(null)] string value) { }")]
    [TestCase(UnmarkedTaggedAndDefaultConstructors)]
    [TestCase(UnmarkedInternalTaggedAndMarkedDefaultConstructors)]
    public void UnusedTaggedConstructorWithoutInject_ShouldReportAtTag(string constructors)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                {{constructors}}
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0011"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Does.StartWith("Tag"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Inject").And.Contain("Service").And.Contain("value"));
        AssertCompiles(compilation);
    }

    [Test]
    public void ArrayTagWithoutInject_ShouldReportBothDeclarationErrors()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service([Tag(new[] { 1 })] string value) { }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(["MADI0010", "MADI0011"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void InjectOnDifferentTypeOrUnrelatedAttribute_ShouldNotMarkConstructor()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public sealed class InjectAttribute : System.Attribute { }

            public class Parent
            {
                [Macaron.DependencyInjection.Inject]
                public Parent() { }
            }

            public class Service : Parent
            {
                [Inject]
                public Service([Tag("tagged")] string value) { }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0011"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void MultipleTagsWithoutInject_ShouldReportEachAttributeOnce()
    {
        var source =
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service([Tag("first")] string first, [Tag("second")] string second) { }
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var attributeTexts = diagnostics.Select(diagnostic =>
        {
            var span = diagnostic.Location.SourceSpan;

            return source.Substring(span.Start, span.Length);
        });

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0011", "MADI0011"]));
        Assert.That(attributeTexts, Is.EquivalentTo(["Tag(\"first\")", "Tag(\"second\")"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void RepeatedUnmarkedConstructorUses_ShouldReportOnceAndExcludeFactory()
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service([Tag("tagged")] string value) { }
                public Service() { }
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

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0011"]));
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();
        Assert.That(factories, Does.Not.Contain("typeof(global::Service)").And.Contain("typeof(global::Valid)"));
        AssertCompiles(compilation);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ReferencedTaggedConstructorWithoutInject_ShouldReportAtRegistration(bool singleType, bool hasDefaultConstructor)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                {{(hasDefaultConstructor ? "[Inject] public Service() { }" : "")}}
                public Service([Tag("tagged")] string value) { }
            }
            """, assemblyName: "ExternalServices"
        ));
        var typeArguments = singleType ? "Service" : "object, Service";
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar) => registrar.RegisterTransient<{{typeArguments}}>();
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0011"));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo("Service"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Inject").And.Contain("Service").And.Contain("value"));
        AssertCompiles(compilation);
    }

    [TestCase("new int[] { 1, 2 }")]
    [TestCase("new[] { \"first\", \"second\" }")]
    [TestCase("new object[] { 1, typeof(string) }")]
    [TestCase("new int[0]")]
    [TestCase("(object)new[] { 1 }")]
    public void UnusedArrayTag_ShouldReportErrorAtValueExpression(string expression)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;
            using DependencyTag = Macaron.DependencyInjection.TagAttribute;

            public class Service
            {
                [Inject]
                public Service([DependencyTag(value: {{expression}})] string value) { }
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0010"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo(expression));
        Assert.That(diagnostic.GetMessage(), Does.Contain("value").And.Contain("Service"));
        AssertCompiles(compilation);
    }

    [TestCase("[Inject] public void Method([Tag(new[] { 1 })] string value) { }")]
    [TestCase("public delegate void Callback([Tag(new[] { 1 })] string value);")]
    [TestCase("public string this[[Tag(new[] { 1 })] string value] => value;")]
    [TestCase("public void Method() { void Local([Tag(new[] { 1 })] string value) { } }")]
    public void ArrayTagDeclaration_ShouldBeCheckedWithoutRegistrationOrInject(string member)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                {{member}}
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0010"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void RepeatedSourceUses_ShouldReportDeclarationOnceAndPreserveValidFactory()
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                [Inject]
                public Service([Tag(new[] { 1 })] string value) { }
            }

            public sealed class Valid { }

            public static class Probe
            {
                public static bool Run()
                {
                    return Macaron.DependencyInjection.Generated.Factories.Get<Valid>() != null;
                }

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

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0010"]));
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Not.Contain("typeof(global::Service)")
        );
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReferencedArrayTag_ShouldReportAtRegistration(bool singleType)
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                [Inject]
                public Service([Tag(new[] { 1 })] string value) { }
            }
            """, assemblyName: "ExternalServices"
        ));
        var typeArguments = singleType ? "Service" : "object, Service";
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar) => registrar.RegisterTransient<{{typeArguments}}>();
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0010"));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo("Service"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("value").And.Contain("Service"));
        AssertCompiles(compilation);
    }

    [TestCase("[Tag(new[] { Missing })]")]
    [TestCase("[Tag(new[] { 1 }, 2)]")]
    [TestCase("[Tag(Value = new[] { 1 })]")]
    [TestCase("[Tag(new[] { 1 }), Tag(new[] { 2 })]")]
    public void CompilerRejectedAttribute_ShouldNotProduceGeneratorDiagnostic(string attribute)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service({{attribute}} string value) { }
            }
            """
        );

        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void TaggedMethodWithoutInject_ShouldRequireInject()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public void Method([Tag("tagged")] string value) { }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0011" }));
        AssertCompiles(compilation);
    }

    [Test]
    public void UnrelatedArrayAttributeAndRuntimeArrayTags_ShouldRemainValid()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class TagAttribute : System.Attribute
            {
                public TagAttribute(object value) { }
            }

            public sealed class Service
            {
                public Service([Tag(new[] { 1 })] string value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var tag = new[] { 1 };
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterInstance(instance: "tagged", tag: tag));

                    return container.Contains<string>(tag) && container.Resolve<string>(tag) == "tagged";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
