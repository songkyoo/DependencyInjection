using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class MethodInjectionDiagnosticTests
{
    [TestCase("[Inject] public static void Initialize() { }", "MADI0015")]
    [TestCase("[Inject] public int Initialize() => 1;", "MADI0015")]
    [TestCase("[Inject] public void Initialize<T>() { }", "MADI0015")]
    [TestCase("[Inject] public async void Initialize() { await System.Threading.Tasks.Task.Yield(); }", "MADI0015")]
    [TestCase("public int Value { get; [Inject] set; }", "MADI0015")]
    [TestCase("public void Method() { [Inject] void Local() { } }", "MADI0015")]
    [TestCase("[Inject] public void Initialize(ref int value) { }", "MADI0008")]
    [TestCase("[Inject] public void Initialize(dynamic value) { }", "MADI0002")]
    [TestCase("[Inject] public void Initialize(System.Span<int> value) { }", "MADI0008")]
    [TestCase("public void Initialize([Assisted] int value) { }", "MADI0011")]
    [TestCase("[Inject] public void Initialize([Tag, Assisted] int value) { }", "MADI0013")]
    [TestCase("[Inject] public Service() { } [Inject] public void Initialize() { }", "MADI0005")]
    [TestCase("[Inject] public void First() { } [Inject] public void Second() { }", "MADI0005")]
    public void UnusedInvalidDeclaration_ShouldBeReportedByAnalyzerAlone(string declaration, string expectedId)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declaration}} }
            """
        );
        var diagnostics = Analyze(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]),
            string.Join(Environment.NewLine, diagnostics)
        );
        Assert.That(diagnostics.Single().Location.IsInSource, Is.True);
        Assert.That(diagnostics.Single().GetMessage(), Does.EndWith("."));
        AssertCompiles(input);
    }

    [TestCase("[Inject] public void Initialize([Assisted] Missing value) { }")]
    [TestCase("[Inject] public async void Initialize([Assisted] Missing value) { await System.Threading.Tasks.Task.Yield(); }")]
    [TestCase("[Inject, Inject] public void Initialize(ref int value) { }")]
    public void CompilerInvalidDeclaration_ShouldNotProduceLibraryDiagnostics(string declaration)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declaration}} }
            """
        );

        Assert.That(Analyze(input), Is.Empty);
        Assert.That(input.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void GeneratorWithoutAnalyzer_ShouldBlockInvalidMethodOutput()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize(ref int value) { } }

            public class Configuration { public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); } }
            """
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            driver.GetRunResult().Results.Single().GeneratedSources.Any(source =>
            {
                return source.HintName.StartsWith("InjectionContextExtensions.");
            }),
            Is.False
        );
        AssertCompiles(output);
    }

    [TestCase("public")]
    [TestCase("public static")]
    public void ExternalMethodDeclaration_ShouldBeCheckedAtUse(string modifiers)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] {{modifiers}} void Initialize([Assisted] ref int value) { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;
            public class Configuration { public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); } }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo([modifiers == "public" ? "MADI0008" : "MADI0015"])
        );
        Assert.That(diagnostics.Single().AdditionalLocations, Is.Empty);
        Assert.That(
            diagnostics.Single().Location.SourceTree!.GetText().ToString(diagnostics.Single().Location.SourceSpan),
            Is.EqualTo("Service")
        );
    }

    [Test]
    public void AsyncMethodAnalysis_AfterCompilation_ShouldReportUnsupportedShapeWithoutAnalyzerException()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public async void Initialize([Assisted] int value)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """
        );

        // 본문이 먼저 컴파일된 입력에서도 선언 검사에 본문 진단 재조회를 요구하지 않는다.
        AssertCompiles(input);

        var diagnostics = Analyze(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0015"]),
            string.Join(Environment.NewLine, diagnostics)
        );
    }

    [TestCase("[Inject] public async void Initialize([Assisted] int value) { await Missing(); }")]
    [TestCase("[Inject] public int Initialize() { return Missing(); }")]
    public void MethodBodyError_ShouldNotSuppressIndependentDeclarationDiagnostic(string declaration)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declaration}} }
            """
        );
        var diagnostics = Analyze(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0015"]),
            string.Join(Environment.NewLine, diagnostics)
        );
        Assert.That(input.GetDiagnostics().Select(diagnostic => diagnostic.Id), Does.Contain("CS0103"));
    }

    [Test]
    public void TopLevelProgram_ShouldIgnoreSynthesizedEntryPointAndStillAnalyzeDeclaredTypes()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            var service = new Service();

            public class Service { [Inject] public static void Initialize() { } }
            """
        );

        input = input.WithOptions(input.Options.WithOutputKind(OutputKind.ConsoleApplication));

        var diagnostics = Analyze(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0015"]),
            string.Join(Environment.NewLine, diagnostics)
        );
        AssertCompiles(input);
    }
}
