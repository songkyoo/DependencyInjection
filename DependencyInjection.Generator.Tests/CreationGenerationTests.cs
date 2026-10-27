using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationGenerationTests
{
    [TestCase("resolver.For<Service>()")]
    [TestCase("IDependencyResolverExtensions.For<Service>(resolver)")]
    [TestCase("getContext()")]
    [TestCase("resolver?.For<Service>()")]
    public void ForSymbolUse_ShouldGenerateCreateAndShareRegisteredFactory(string expression)
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
                    {{(expression == "getContext()" ? "Func<InjectionContext<Service>> getContext = resolver.For<Service>;" : "")}}

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

    [TestCase(false)]
    [TestCase(true)]
    public void UnregisteredClosedTypes_ShouldSupportExternalAssemblyAndDistinctNames(bool externalType)
    {
        const string declarations =
            """
            namespace A
            {
                public class Service<T> { public Service() { } }
            }

            namespace B
            {
                public class Service<T> { public Service() { } }
            }

            public class Outer<T> { public struct Inner { } }

            public struct Empty { }
            """;
        var external = externalType ? EmitAssembly(CreateCompilation(declarations, "External")) : null;
        var (compilation, sources) = Generate(
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
            """ + (externalType ? "" : declarations),
            additionalReferences: external is null ? [] : [MetadataReference.CreateFromImage(external)]
        );

        Assert.That(Execute<bool>(compilation, external is null ? [] : [external]), Is.True);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(5));
        Assert.That(sources.Select(source => source.HintName).Distinct().Count(), Is.EqualTo(sources.Length));
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, true)]
    public void Parameters_ShouldSelectMarkedConstructorAndResolveOnceInOrder(bool tagged, bool externalType, bool inParameter)
    {
        var declarations =
            $$"""
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                public string Value { get; }

                {{(inParameter ? "public Service(T first, T middle, T last) { Value = \"wrong\"; }" : "")}}

                [Inject]
                public Service([Tag("first")] T first, {{(tagged ? "[Tag(\"middle\")]" : "")}} {{(inParameter ? "in " : "")}}T middle, [Tag("last")] T last)
                {
                    Value = $"{first}|{middle}|{last}";
                }
            }
            """;
        var external = externalType ? EmitAssembly(CreateCompilation(declarations, "External")) : null;
        var (compilation, _) = Generate(
            (externalType ? "using Macaron.DependencyInjection;" : declarations) +
            $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    var calls = 0;
                    using var container = DependencyContainerFactory.Create(r =>
                    {
                        r.RegisterTransient<int>(() => ++calls, tag: "first");
                        r.RegisterTransient<int>(() => ++calls, tag: {{(tagged ? "\"middle\"" : "null")}});
                        r.RegisterTransient<int>(() => ++calls, tag: "last");
                    });

                    return container.For<Service<int>>().Create().Value == "1|2|3" && calls == 3;
                }
            }
            """,
            additionalReferences: external is null ? [] : [MetadataReference.CreateFromImage(external)]
        );

        Assert.That(Execute<bool>(compilation, external is null ? [] : [external]), Is.True);
    }

    [TestCase("null")]
    [TestCase("\"\"")]
    [TestCase("\"한글\"")]
    [TestCase("(byte)1")]
    [TestCase("1")]
    [TestCase("1L")]
    [TestCase("Kind.Value")]
    [TestCase("typeof(int[])")]
    public void Tag_ShouldPreserveValueAndBoxingType(string tag)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public enum Kind { Value }

            public class Service
            {
                public string Value { get; }

                [Inject] public Service([Tag({{tag}})] string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(r =>
                    {
                        r.RegisterSingleton(instance: "expected", tag: {{tag}});
                        r.RegisterSingleton(instance: "wrong", tag: "service");
                        r.RegisterTransient<Service>(tag: "service");
                    });

                    return container.For<Service>().Create().Value == "expected";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OptionalAndParamsDependencies_ShouldAlwaysResolve(bool missing)
    {
        var (compilation, _) = Generate(
            $$"""
            using System;
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
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(r =>
                    {
                        {{(missing ? "" : "r.RegisterSingleton(instance: \"resolved\"); r.RegisterSingleton(instance: new[] { 1, 2 });")}}
                    });

                    try { return container.For<Service>().Create().Value == "resolved2" && !{{missing.ToString().ToLowerInvariant()}}; }
                    catch (InvalidOperationException) { return {{missing.ToString().ToLowerInvariant()}}; }
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
