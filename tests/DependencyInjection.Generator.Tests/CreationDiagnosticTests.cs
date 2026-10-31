using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationDiagnosticTests
{
    private const string InternalConstructor =
        """
        using Macaron.DependencyInjection;

        public class Service
        {
            public string Value { get; }

            [Inject]
            internal Service([Tag("value")] string value) { Value = value; }
        }
        """;

    private const string InternalTag =
        """
        using Macaron.DependencyInjection;

        internal enum Kind { Value }

        public class Service
        {
            public int Value { get; }

            [Inject]
            public Service([Tag(Kind.Value)] int value) { Value = value; }
        }
        """;

    private const string ForUse =
        """
        using Macaron.DependencyInjection;

        public class Configuration
        {
            public void Configure(IDependencyResolver resolver)
            {
                resolver.For<Service>();
            }
        }
        """;

    private static readonly SyntaxTree FriendAccess = CSharpSyntaxTree.ParseText(
        """
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]
        """, InputParseOptions, "AssemblyInfo.cs"
    );

    [TestCase("public Service([Tag] int value) { }", "MADI0011")]
    [TestCase("[Inject] public Service([Tag(new[] { 1 })] int value) { }", "MADI0010")]
    [TestCase("[Inject] public Service([Tag] ref int value) { }", "MADI0008")]
    [TestCase("[Inject] public Service([Tag] dynamic value) { }", "MADI0002")]
    [TestCase("[Inject] private Service() { }", "MADI0018")]
    public void InvalidExternalConstructor_ShouldDiagnoseForLocation(string constructor, string id)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{constructor}}
            }
            """,
            assemblyName: "External"
        ));
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo(id));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.LastIndexOf("Service", StringComparison.Ordinal)));
        Assert.That(diagnostic.AdditionalLocations, Is.Empty);
        Assert.That(sources, Has.Length.EqualTo(2));
        AssertCompiles(compilation);
    }

    [Test]
    public void ExternalInternalConstructor_ShouldBeRejectedWithoutFriendAccess()
    {
        var external = EmitAssembly(CreateCompilation(InternalConstructor, "External"));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            ForUse,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
        Assert.That(sources, Has.Length.EqualTo(2));
        AssertCompiles(compilation);
    }

    [Test]
    public void ExternalInternalConstructor_ShouldExecuteWithConsumerFriendAccess()
    {
        var external = EmitAssembly(CreateCompilation(InternalConstructor, "External").AddSyntaxTrees(FriendAccess));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterInstance(instance: "expected", tag: "value"));

                    return container.For<Service>().Create().Value;
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<string>(compilation, external), Is.EqualTo("expected"));
    }

    [Test]
    public void ExternalInternalTag_ShouldBeRejectedWithoutFriendAccess()
    {
        var external = EmitAssembly(CreateCompilation(InternalTag, "External"));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            ForUse,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0012"]));
        Assert.That(sources, Has.Length.EqualTo(2));
        AssertCompiles(compilation);
    }

    [Test]
    public void ExternalInternalTag_ShouldResolveWithConsumerFriendAccess()
    {
        var external = EmitAssembly(CreateCompilation(
            InternalTag,
            assemblyName: "External"
        ).AddSyntaxTrees(FriendAccess));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterInstance(instance: 42, tag: Kind.Value));

                    return container.For<Service>().Create().Value;
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<int>(compilation, external), Is.EqualTo(42));
    }
}
