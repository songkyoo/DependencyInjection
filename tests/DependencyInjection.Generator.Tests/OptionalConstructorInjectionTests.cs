using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class OptionalConstructorInjectionTests
{
    private const string Services =
        """
        #nullable enable
        using Macaron.DependencyInjection;

        public interface IView
        {
            string Value { get; }
        }
        public interface IDependency { }
        public sealed class Dependency : IDependency { }

        public sealed class Service : IView
        {
            public string Value { get; }

            [Inject]
            public Service(
                [Tag("optional")] IDependency? dependency = null,
                [Tag("name")] string? name = "guest",
                [Tag("count")] int? count = 3,
                [Tag("limit")] int limit = 5)
            {
                Value = $"{dependency is null}|{name}|{count}|{limit}";
            }
        }
        """;

    [Test]
    public void MissingDependencies_ShouldSupplyNullAndDeclaredDefaultsAcrossConstructionPaths()
    {
        var input = WithServices(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string[] Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Service>();
                        registrar.RegisterTransient<IView, Service>();
                    });

                    return new[]
                    {
                        container.Resolve<Service>().Value,
                        container.Resolve<IView>().Value,
                        container.For<Service>().Create().Value,
                    };
                }
            }
            """
        );
        var (compilation, _) = Generate(input);

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(["True|guest|3|5", "True|guest|3|5", "True|guest|3|5"]));
        AssertNoWarnings(compilation);
    }

    [Test]
    public void RegisteredDependencies_ShouldOverrideDefaultsAndResolveEachParameterOnce()
    {
        var input = WithServices(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string[] Run()
                {
                    var calls = 0;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IDependency>(new Dependency(), tag: "optional");
                        registrar.RegisterSingleton(instance: "registered", tag: "name");
                        registrar.RegisterTransient<int>(() => ++calls, tag: "count");
                        registrar.RegisterSingleton(instance: 9, tag: "limit");
                        registrar.RegisterTransient<Service>();
                        registrar.RegisterTransient<IView, Service>();
                    });

                    return new[]
                    {
                        container.Resolve<Service>().Value,
                        container.Resolve<IView>().Value,
                        container.For<Service>().Create().Value,
                        calls.ToString(),
                    };
                }
            }
            """
        );
        var (compilation, _) = Generate(input);

        Assert.That(
            Execute<string[]>(compilation),
            Is.EqualTo(["False|registered|1|9", "False|registered|2|9", "False|registered|3|9", "3"])
        );
        AssertNoWarnings(compilation);
    }

    [Test]
    public void MissingTaggedDependencies_ShouldUseDefaultsWithoutMatchingDefaultTag()
    {
        var input = WithServices(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IDependency>(new Dependency());
                        registrar.RegisterSingleton(instance: "wrong tag");
                        registrar.RegisterSingleton(instance: 99);
                    });

                    return container.For<Service>().Create().Value;
                }
            }
            """
        );
        var (compilation, _) = Generate(input);

        Assert.That(Execute<string>(compilation), Is.EqualTo("True|guest|3|5"));
    }

    [TestCase("string?")]
    [TestCase("int?")]
    public void ClosedNullableDefault_ShouldSupplyNullWithoutNullableServiceRegistration(string typeArgument)
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable
            using Macaron.DependencyInjection;

            public sealed class Service<T>
            {
                public T Value { get; }
                public Service(T value = default!) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterTransient<Service<{{typeArgument}}>>());

                    return container.Resolve<Service<{{typeArgument}}>>().Value is null
                        && container.For<Service<{{typeArgument}}>>().Create().Value is null;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        AssertNoWarnings(compilation);
    }

    [Test]
    public void InNullableValueParameters_ShouldPreserveTypesOverloadAndResolutionOrder()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                public Service(int? first, int? second, int? missing) { Value = "wrong overload"; }

                [Inject]
                public Service([Tag("first")] in int? first = null, [Tag("second")] in int? second = null, in int? missing = 3)
                {
                    Value = $"{first}|{second}|{missing}";
                }
            }

            public static class Probe
            {
                public static string[] Run()
                {
                    var calls = 0;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<int>(() => ++calls, tag: "first");
                        registrar.RegisterTransient<int>(() => ++calls, tag: "second");
                        registrar.RegisterTransient<Service>();
                    });

                    return new[]
                    {
                        container.Resolve<Service>().Value,
                        container.For<Service>().Create().Value,
                        calls.ToString(),
                    };
                }
            }
            """
        );

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(["1|2|3", "3|4|3", "4"]));
        AssertNoWarnings(compilation);
    }

    [Test]
    public void MissingInNullableValueWithoutDefault_ShouldKeepRequiredResolve()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public struct Service
            {
                public bool IsNull { get; }
                [Inject] public Service(in int? count) { IsNull = count is null; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service>());

                    try { container.Resolve<Service>(); return false; }
                    catch (System.InvalidOperationException) { }

                    try { container.For<Service>().Create(); return false; }
                    catch (System.InvalidOperationException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ExternalClosedGeneric_ShouldPreserveNullableInParameterAndDecimalDefault()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            #nullable enable
            public sealed class Service<T>
            {
                public T? Value { get; }
                public decimal Amount { get; }

                public Service(in T? value = default, decimal amount = 1.25M)
                {
                    Value = value;
                    Amount = amount;
                }
            }
            """, "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Service<string>>();
                        registrar.RegisterTransient<Service<int?>>();
                    });
                    var reference = container.Resolve<Service<string>>();
                    var value = container.Resolve<Service<int?>>();

                    return reference.Value is null && reference.Amount == 1.25M
                        && value.Value is null && value.Amount == 1.25M
                        && container.For<Service<string>>().Create().Value is null
                        && container.For<Service<int?>>().Create().Value is null;
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<bool>(compilation, external), Is.True);
        AssertNoWarnings(compilation);
    }

    [Test]
    public void UnconstrainedNullableTypeParameterClosedWithInt_ShouldRemainRequired()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Service<T>
            {
                public Service(T? value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    try { container.For<Service<int>>().Create(); return false; }
                    catch (InvalidOperationException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        AssertNoWarnings(compilation);
    }

    [TestCase("System.Collections.Generic.List<string?>")]
    [TestCase("string[]")]
    public void NestedNullabilityAndParamsElementTypes_ShouldNotMakeServiceOptional(string type)
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public Service({{type}} value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    try { container.For<Service>().Create(); return false; }
                    catch (InvalidOperationException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ObliviousReferenceParameter_ShouldRemainRequired()
    {
        var (compilation, _) = Generate(
            """
            #nullable disable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public Service(string value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    try { container.For<Service>().Create(); return false; }
                    catch (InvalidOperationException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void NullableAssistedAndParams_ShouldRemainCallerSupplied()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public sealed class Dependency { }

            public sealed class Service
            {
                public bool Matches { get; }

                [Inject]
                public Service([Assisted] string? supplied, Dependency? dependency = null, [Assisted] params int[] values)
                {
                    Matches = supplied is null && dependency is null && values.Length == 0;
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    return container.For<Service>().Create(supplied: null).Matches;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        AssertNoWarnings(compilation);
    }

    [TestCase("string", "string?")]
    [TestCase("string?", "string")]
    public void EquivalentGenericAnnotationsWithoutDefaults_ShouldShareRequiredFactory(
        string firstArgument,
        string secondArgument
    )
    {
        var (compilation, sources) = Generate(
            $$"""
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Service<T>
            {
                public Service(T value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Service<{{firstArgument}}>>(tag: "first");
                        registrar.RegisterTransient<Service<{{secondArgument}}>>(tag: "second");
                    });

                    try { container.Resolve<Service<{{firstArgument}}>>("first"); return false; }
                    catch (InvalidOperationException) { }

                    try { container.Resolve<Service<{{secondArgument}}>>("second"); return false; }
                    catch (InvalidOperationException) { }

                    try { container.For<Service<{{firstArgument}}>>().Create(); return false; }
                    catch (InvalidOperationException) { }

                    try { container.For<Service<{{secondArgument}}>>().Create(); return false; }
                    catch (InvalidOperationException) { return true; }
                }
            }
            """
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories.Split("factories.Add(").Length - 1, Is.EqualTo(1));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("", 3)]
    [TestCase("registrar.RegisterSingleton(instance: 42);", 42)]
    public void NullableValueDefault_ShouldPreserveSelectedValueOverload(string dependencyRegistration, int expected)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public int? Value { get; }
                public Service(int value) { Value = -1; }
                [Inject] public Service(int? value = 3) { Value = value; }
            }

            public static class Probe
            {
                public static int?[] Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        {{dependencyRegistration}}
                        registrar.RegisterTransient<Service>();
                    });

                    return new[] { container.Resolve<Service>().Value, container.For<Service>().Create().Value };
                }
            }
            """
        );

        Assert.That(Execute<int?[]>(compilation), Is.EqualTo(new int?[] { expected, expected }));
    }

    private static CSharpCompilation WithServices(string consumer)
    {
        return CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(Services, InputParseOptions, "Services.cs"));
    }

    private static void AssertNoWarnings(Compilation compilation)
    {
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            Is.Empty
        );
    }
}
