using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class InjectRequirementTests
{
    [TestCase("public", "Tag")]
    [TestCase("internal", "Assisted")]
    [TestCase("private", "Tag")]
    [TestCase("private", "Assisted")]
    [TestCase("protected", "Tag")]
    [TestCase("protected", "Assisted")]
    [TestCase("private protected", "Tag")]
    [TestCase("private protected", "Assisted")]
    public void UnusedConstructorAndMethod_WithoutInject_ShouldDiagnoseRegardlessOfAccessibility(
        string accessibility,
        string attribute
    )
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{accessibility}} Service([{{attribute}}] int value) { }
                {{accessibility}} void Initialize([{{attribute}}] int value) { }
            }
            """
        );
        var diagnostics = Analyze(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0011", "MADI0011" }));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)),
            Is.All.EqualTo(attribute));
        AssertCompiles(input);
    }

    [TestCase("[Inject] private Service([Tag] int first, [Assisted] int second) { }", "MADI0018")]
    [TestCase("[Inject] private void Initialize([Tag] int first, [Assisted] int second) { }", "MADI0018")]
    [TestCase("[Inject] protected void Initialize([Tag] int first, [Assisted] int second) { }", "MADI0018")]
    [TestCase("[Inject] public static void Initialize([Tag] int first, [Assisted] int second) { }", "MADI0015")]
    [TestCase("[Inject] public int Initialize([Tag] int first, [Assisted] int second) => first;", "MADI0015")]
    [TestCase("[Inject] public void Initialize<T>([Tag] int first, [Assisted] int second) { }", "MADI0015")]
    public void InvalidInjectDeclaration_ShouldSatisfyParameterRequirementAndKeepOriginalDiagnostic(
        string declaration,
        string expectedId
    )
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{declaration}}
            }
            """
        );

        Assert.That(Analyze(input).Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { expectedId }));
        AssertCompiles(input);
    }

    [TestCase("Tag")]
    [TestCase("Assisted")]
    public void ExternalPrivateConstructorAndMethod_WithoutInject_ShouldDiagnoseAtEachUse(string attribute)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }

                private Service([{{attribute}}] int value) { }

                private void Initialize([{{attribute}}] int value) { }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Service>();
                    resolver.For<Service>();
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(Enumerable.Repeat("MADI0011", 4)));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start).Distinct().Count(), Is.EqualTo(2));
        Assert.That(sources.Any(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.False);
        AssertCompiles(compilation);
    }

    [Test]
    public void BaseInjectDeclaration_ShouldNotSatisfyOverrideParameterRequirement()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                [Inject] public virtual void Initialize(int first, int second) { }
            }

            public class Derived : Base
            {
                public override void Initialize([Tag] int first, [Assisted] int second) { }
            }
            """
        );

        Assert.That(Analyze(input).Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0011", "MADI0011"]));
        AssertCompiles(input);
    }
}
