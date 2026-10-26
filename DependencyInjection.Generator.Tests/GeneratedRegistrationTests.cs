using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class GeneratedRegistrationTests
{
    [Test]
    public void EmptyInput_ShouldGenerateInternalStaticRegistrationAndFactories()
    {
        var (compilation, sources) = Generate("");

        AssertCompiles(compilation);
        Assert.That(sources, Has.Length.EqualTo(2));

        var extensions = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions");
        var factories = compilation.GetTypeByMetadataName("Macaron.DependencyInjection.Generated.Factories");

        Assert.That(extensions, Is.Not.Null);
        Assert.That(factories, Is.Not.Null);
        Assert.That(extensions!.IsStatic, Is.True);
        Assert.That(factories!.IsStatic, Is.True);
        Assert.That(extensions.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal));
        Assert.That(factories.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal));

        var registers = extensions.GetMembers("Register").OfType<IMethodSymbol>().OrderBy(method => method.Arity).ToArray();

        Assert.That(registers, Has.Length.EqualTo(2));

        var singleType = registers[0];
        var register = registers[1];

        Assert.That(singleType.IsExtensionMethod, Is.True);
        Assert.That(singleType.TypeParameters.Single().HasNotNullConstraint, Is.True);
        Assert.That(singleType.TypeParameters.Single().HasConstructorConstraint, Is.False);
        Assert.That(register.IsExtensionMethod, Is.True);
        Assert.That(
            register.TypeParameters.Select(parameter => parameter.Name),
            Is.EqualTo(["TService", "TImplementation"])
        );
        Assert.That(register.TypeParameters.All(parameter => parameter.HasNotNullConstraint), Is.True);
        Assert.That(register.TypeParameters[1].HasConstructorConstraint, Is.False);
    }

    [TestCase("Register", "IService", "Service")]
    [TestCase("Register", "BaseService", "DerivedService")]
    [TestCase("Register", "ValueService", "ValueService")]
    [TestCase("RegisterSingleton", "IService", "Service")]
    [TestCase("RegisterSingleton", "BaseService", "DerivedService")]
    [TestCase("RegisterSingleton", "ValueService", "ValueService")]
    [TestCase("RegisterScoped", "IService", "Service")]
    [TestCase("RegisterScoped", "BaseService", "DerivedService")]
    [TestCase("RegisterScoped", "ValueService", "ValueService")]
    [TestCase("RegisterTransient", "IService", "Service")]
    [TestCase("RegisterTransient", "BaseService", "DerivedService")]
    [TestCase("RegisterTransient", "ValueService", "ValueService")]
    public void RegistrationExtension_ShouldAcceptAssignableImplementationWithoutDefaultConstructorConstraint(
        string registrationMethod,
        string serviceType,
        string implementationType
    )
    {
        var requiredArguments = registrationMethod == "Register" ? "Lifetime.Scoped" : "";
        var configuredArguments = registrationMethod == "Register" ? "Lifetime.Scoped," : "";
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public sealed class Service : IService
            {
                public Service(string value) { }
            }

            public class BaseService { }

            public sealed class DerivedService : BaseService { }

            public struct ValueService : IService { }

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.{{registrationMethod}}<{{serviceType}}, {{implementationType}}>({{requiredArguments}});
                    registrar.{{registrationMethod}}<{{serviceType}}, {{implementationType}}>(
                        {{configuredArguments}}
                        tag: "service",
                        cleanup: _ => { }
                    );
                }
            }
            """;
        var (compilation, _) = Generate(source);

        AssertCompiles(compilation);

        var input = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Input.cs");
        var invocations = input.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
        var semanticModel = compilation.GetSemanticModel(input);

        foreach (var invocation in invocations)
        {
            var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

            Assert.That(method, Is.Not.Null);
            Assert.That(method!.Name, Is.EqualTo(registrationMethod));
            Assert.That(
                method.ContainingType.ToDisplayString(),
                Is.EqualTo("Macaron.DependencyInjection.DependencyRegistrarExtensions")
            );
        }
    }

    [TestCase("Register")]
    [TestCase("RegisterSingleton")]
    [TestCase("RegisterScoped")]
    [TestCase("RegisterTransient")]
    public void RegistrationExtension_ShouldRejectImplementationNotAssignableToService(string registrationMethod)
    {
        var requiredArguments = registrationMethod == "Register" ? "Lifetime.Transient" : "";
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public sealed class Unrelated { }

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.{{registrationMethod}}<IService, Unrelated>({{requiredArguments}});
                }
            }
            """
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors.Select(diagnostic => diagnostic.Id), Is.EqualTo(["CS0311"]));
    }

    [Test]
    public void FactoriesHintName_ShouldBeStableForAssemblyIdentity()
    {
        var (_, first) = Generate("", "FirstConsumer");
        var (_, repeated) = Generate("", "FirstConsumer");
        var (_, other) = Generate("", "OtherConsumer");

        var firstHint = first.Single(source => source.HintName.StartsWith("Factories.")).HintName;
        var repeatedHint = repeated.Single(source => source.HintName.StartsWith("Factories.")).HintName;
        var otherHint = other.Single(source => source.HintName.StartsWith("Factories.")).HintName;

        Assert.That(firstHint, Does.Match(@"^Factories\.[0-9a-f]{8}\.g\.cs$"));
        Assert.That(repeatedHint, Is.EqualTo(firstHint));
        Assert.That(otherHint, Is.Not.EqualTo(firstHint));
    }

    [Test]
    public void RegistrationHintName_ShouldRemainStableForSameGeneratorAcrossConsumerAssemblies()
    {
        var (_, first) = Generate("", "FirstConsumer");
        var (_, other) = Generate("", "OtherConsumer");

        var firstHint = first.Single(source => source.HintName.StartsWith("DependencyRegistrarExtensions.")).HintName;
        var otherHint = other.Single(source => source.HintName.StartsWith("DependencyRegistrarExtensions.")).HintName;

        Assert.That(firstHint, Does.Match(@"^DependencyRegistrarExtensions\.[0-9a-f]{8}\.g\.cs$"));
        Assert.That(otherHint, Is.EqualTo(firstHint));
    }

    [Test]
    public void EmptyFactories_ShouldRejectMissingFactory()
    {
        var (compilation, _) = Generate(
            """
            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        Macaron.DependencyInjection.Generated.Factories.Get<object>();
                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """,
            assemblyName: "EmptyFactoryLookup"
        );

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        stream.Position = 0;

        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var rejectedMissingFactory = assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);

        Assert.That(rejectedMissingFactory, Is.True);
    }
}
