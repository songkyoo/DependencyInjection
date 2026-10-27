using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.DependencyInjection.Generator.Tests;

internal static class GeneratorTestHelper
{
    internal static readonly CSharpParseOptions InputParseOptions = new(LanguageVersion.CSharp9);

    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(IDependencyRegistrar).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    internal static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "Consumer",
        params MetadataReference[] additionalReferences
    )
    {
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, InputParseOptions, path: "Input.cs")],
            references: References.Concat(additionalReferences),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    internal static GeneratorDriver CreateDriver(bool trackIncrementalSteps = false)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new DependencyInjectionGenerator().AsSourceGenerator()],
            parseOptions: InputParseOptions,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: trackIncrementalSteps
            )
        );
    }

    internal static (Compilation Compilation, ImmutableArray<GeneratedSourceResult> Sources) Generate(
        string source,
        string assemblyName = "Consumer",
        params MetadataReference[] additionalReferences
    )
    {
        var (output, sources, diagnostics) = GenerateWithDiagnostics(source, assemblyName, additionalReferences);

        Assert.That(diagnostics, Is.Empty);

        return (output, sources);
    }

    internal static (Compilation Compilation, ImmutableArray<GeneratedSourceResult> Sources) Generate(CSharpCompilation input)
    {
        var (output, sources, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics, Is.Empty);

        return (output, sources);
    }

    internal static (Compilation Compilation, ImmutableArray<GeneratedSourceResult> Sources, ImmutableArray<Diagnostic> Diagnostics)
        GenerateWithDiagnostics(
            string source,
            string assemblyName = "Consumer",
            params MetadataReference[] additionalReferences
        )
    {
        return GenerateWithDiagnostics(CreateCompilation(source, assemblyName, additionalReferences));
    }

    internal static (Compilation Compilation, ImmutableArray<GeneratedSourceResult> Sources, ImmutableArray<Diagnostic> Diagnostics)
        GenerateWithDiagnostics(CSharpCompilation compilation)
    {
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var diagnostics
        );

        return (output, driver.GetRunResult().Results.Single().GeneratedSources, diagnostics);
    }

    internal static byte[] EmitAssembly(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        return stream.ToArray();
    }

    internal static void AssertCompiles(Compilation compilation)
    {
        EmitAssembly(compilation);
    }

    internal static T Execute<T>(Compilation compilation, params byte[][] dependencies)
    {
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);

        try
        {
            foreach (var dependency in dependencies)
            {
                using var dependencyStream = new MemoryStream(dependency);

                context.LoadFromStream(dependencyStream);
            }

            using var stream = new MemoryStream(EmitAssembly(compilation));
            var assembly = context.LoadFromStream(stream);

            return (T)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
