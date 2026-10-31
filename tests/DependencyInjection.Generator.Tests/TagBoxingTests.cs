using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class TagBoxingTests
{
    [TestCase("bool", "true")]
    [TestCase("char", "'x'")]
    [TestCase("byte", "(byte)1")]
    [TestCase("sbyte", "(sbyte)-1")]
    [TestCase("short", "(short)-1")]
    [TestCase("ushort", "(ushort)1")]
    [TestCase("int", "1")]
    [TestCase("uint", "1U")]
    [TestCase("long", "1L")]
    [TestCase("ulong", "ulong.MaxValue")]
    [TestCase("float", "1.25F")]
    [TestCase("double", "1.25D")]
    [TestCase("Kind", "Kind.Main")]
    public void Create_ShouldReuseBoxedTagAcrossRequiredAndOptionalParametersAndCalls(string tagType, string expression)
    {
        var (compilation, _) = Generate(
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public enum Kind : byte { Main = 1 }

            public sealed class Service
            {
                [Inject]
                public Service([Tag({{expression}})] string first, [Tag({{expression}})] string second = null) { }
            }

            public sealed class RecordingResolver : IDependencyResolver
            {
                public object FirstTag;
                public bool SameReferences = true;
                public bool ValidValues = true;
                public int ResolveCalls;
                public int ContainsCalls;

                public T Resolve<T>(object tag = null)
                {
                    Record(tag);
                    ResolveCalls++;
                    return (T)(object)"value";
                }

                public bool Contains<T>(object tag = null)
                {
                    Record(tag);
                    ContainsCalls++;
                    return true;
                }

                private void Record(object tag)
                {
                    FirstTag ??= tag;
                    SameReferences &= ReferenceEquals(FirstTag, tag);
                    ValidValues &= tag.GetType() == typeof({{tagType}}) && tag.Equals({{expression}});
                }
            }

            public static class Probe
            {
                public static (bool SameReferences, bool ValidValues, int Resolves, int Contains) Run()
                {
                    var resolver = new RecordingResolver();
                    var context = resolver.For<Service>();

                    context.Create();
                    context.Create();

                    return (resolver.SameReferences, resolver.ValidValues, resolver.ResolveCalls, resolver.ContainsCalls);
                }
            }
            """
        );
        var result = Execute<(bool SameReferences, bool ValidValues, int Resolves, int Contains)>(compilation);

        Assert.That(result.SameReferences, Is.True);
        Assert.That(result.ValidValues, Is.True);
        Assert.That(result.Resolves, Is.EqualTo(4));
        Assert.That(result.Contains, Is.EqualTo(2));
    }

    [TestCase("public Service([Tag(Kind.Main)] in string value) { }", "factory(resolver, ref collector);")]
    [TestCase("public Service([Assisted] int count, [Tag(Kind.Main)] in string value) { }", "resolver.For<Service>().Create(1);")]
    [TestCase("public void Initialize([Tag(Kind.Main)] in string value) { }", "resolver.For<Service>().Inject(instance);")]
    public void GeneratedEntryPoint_ShouldReuseBoxedEnumTagAcrossCalls(string member, string operation)
    {
        var isFactory = operation.StartsWith("factory");
        var preparation = isFactory
            ? "var factory = Macaron.DependencyInjection.Generated.Factories.Get<Service>();"
            : operation.Contains("Inject") ? "var instance = new Service();" : "";
        var (compilation, _) = Generate(
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public enum Kind { Main = 1 }

            public sealed class Service
            {
                [Inject]
                {{member}}
            }

            public sealed class RecordingResolver : IDependencyResolver
            {
                public object FirstTag;
                public bool SameReferences = true;
                public int Calls;

                public T Resolve<T>(object tag = null)
                {
                    FirstTag ??= tag;
                    SameReferences &= ReferenceEquals(FirstTag, tag);
                    Calls++;
                    return (T)(object)"value";
                }

                public bool Contains<T>(object tag = null) => true;
            }

            public static class Probe
            {
                public static (bool SameReferences, int Calls) Run()
                {
                    var resolver = new RecordingResolver();
                    var collector = new CleanupCollector();
                    {{preparation}}
                    {{(isFactory ? "Func<InjectionContext<Service>> discover = resolver.For<Service>;" : "")}}

                    {{operation}}
                    {{operation}}

                    return (resolver.SameReferences, resolver.Calls);
                }
            }
            """
        );

        var result = Execute<(bool SameReferences, int Calls)>(compilation);

        Assert.That(result.SameReferences, Is.True);
        Assert.That(result.Calls, Is.EqualTo(2));
    }
}
