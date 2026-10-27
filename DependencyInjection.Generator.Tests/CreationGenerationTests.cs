using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationGenerationTests
{
    private const string ClosedTypes =
        """
        namespace A { public class Service<T> { } }
        namespace B { public class Service<T> { } }

        public class Outer<T>
        {
            public struct Inner { }
        }

        public struct Empty { }
        """;

    private const string ClosedTypeConsumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static bool Run()
            {
                using var container = DependencyContainerFactory.Create(_ => { });

                return container.For<A.Service<int>>().Create() != null
                    && container.For<A.Service<string>>().Create() != null
                    && container.For<B.Service<int>>().Create() != null
                    && container.For<Outer<string>.Inner>().Create().Equals(new Outer<string>.Inner())
                    && container.For<Empty>().Create().Equals(new Empty());
            }
        }
        """;

    [TestCase("resolver.For<Service>()")]
    [TestCase("IDependencyResolverExtensions.For<Service>(resolver)")]
    [TestCase("resolver?.For<Service>()")]
    public void ForInvocation_ShouldGenerateCreateAndShareRegisteredFactory(string expression)
    {
        var (compilation, sources) = Generate(
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public class Service { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(r => r.RegisterTransient<Service>());
                    IDependencyResolver resolver = container;
                    return {{expression}}.Create() is Service;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories.Split("factories.Add(").Length - 1, Is.EqualTo(1));

        var creation = sources.Single(source => source.HintName.StartsWith("InjectionContextExtensions."));

        Assert.That(creation.HintName, Does.Match(@"^InjectionContextExtensions.Service_0\.[0-9a-f]{8}\.g\.cs$"));
        Assert.That(creation.SourceText.ToString(), Does.Contain("var resolver = context.Resolver;"));
        Assert.That(creation.SourceText.ToString(), Does.Contain("return new global::Service();"));
    }

    [Test]
    public void ForMethodGroup_ShouldDiscoverUnregisteredTarget()
    {
        var (compilation, sources) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    IDependencyResolver resolver = container;
                    Func<InjectionContext<Service>> getContext = resolver.For<Service>;

                    return getContext().Create() is Service;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
    }

    [Test]
    public void UnregisteredClosedTypes_ShouldGenerateDistinctCreationFiles()
    {
        var input = CreateCompilation(ClosedTypeConsumer)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(ClosedTypes, InputParseOptions, "Types.cs"));
        var (compilation, sources) = Generate(input);

        Assert.That(Execute<bool>(compilation), Is.True);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(5));
        Assert.That(sources.Select(source => source.HintName).Distinct().Count(), Is.EqualTo(sources.Length));
    }

    [Test]
    public void ExternalClosedTypes_ShouldGenerateAndExecuteInConsumerAssembly()
    {
        var external = EmitAssembly(CreateCompilation(ClosedTypes, "ExternalServices"));
        var (compilation, sources) = Generate(ClosedTypeConsumer, additionalReferences: MetadataReference.CreateFromImage(external));

        Assert.That(Execute<bool>(compilation, external), Is.True);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(5));
        Assert.That(sources.Select(source => source.HintName).Distinct().Count(), Is.EqualTo(sources.Length));
    }

    [Test]
    public void OptionalAndParamsParameters_ShouldResolveRegisteredValues()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public string Value { get; }

                public Service(string value = "fallback", params int[] values)
                {
                    Value = value + values.Length;
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "resolved");
                        registrar.RegisterSingleton(instance: new[] { 1, 2 });
                    });

                    return container.For<Service>().Create().Value;
                }
            }
            """
        );

        Assert.That(Execute<string>(compilation), Is.EqualTo("resolved2"));
    }

    [Test]
    public void MissingOptionalDependency_ShouldFailInsteadOfUsingDefaultValue()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service(string value = "fallback") { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    try { container.For<Service>().Create(); }
                    catch (InvalidOperationException) { return true; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void MissingParamsArray_ShouldFailInsteadOfCreatingEmptyArray()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service(params int[] values) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    try { container.For<Service>().Create(); }
                    catch (InvalidOperationException) { return true; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void DefaultContext_ShouldFailEvenWithoutDependencies()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service { }

            public static class Probe
            {
                public static bool Run()
                {
                    Func<IDependencyResolver, InjectionContext<Service>> forService = IDependencyResolverExtensions.For<Service>;

                    try { default(InjectionContext<Service>).Create(); return false; }
                    catch (InvalidOperationException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void UnrelatedForMethod_ShouldNotDiscoverTarget()
    {
        var (compilation, sources) = Generate(
            """
            public class Fake { public static void For<T>() { } }
            public class Probe { public void Run() { Fake.For<object>(); } }
            """
        );

        Assert.That(sources, Has.Length.EqualTo(2));
        AssertCompiles(compilation);
    }
}
