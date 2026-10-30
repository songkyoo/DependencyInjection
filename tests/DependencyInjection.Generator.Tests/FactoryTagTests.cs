using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryTagTests
{
    private const string TaggedConstructors =
        """
        using Macaron.DependencyInjection;
        using DependencyTag = Macaron.DependencyInjection.TagAttribute;

        public interface IService
        {
            string Value { get; }
        }

        public sealed class Service<T> : IService
        {
            public string Value { get; }

            [Inject]
            public Service(
                [DependencyTag("dependency")] T tagged,
                T untagged,
                [DependencyTag] T defaultTag,
                [DependencyTag(null)] T nullTag,
                [DependencyTag("")] T emptyTag)
            {
                Value = $"{tagged}|{untagged}|{defaultTag}|{nullTag}|{emptyTag}";
            }
        }
        """;

    private const string TaggedConsumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string[] Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                {
                    registrar.RegisterSingleton(instance: "default");
                    registrar.RegisterSingleton(instance: "tagged", tag: "dependency");
                    registrar.RegisterSingleton(instance: "empty", tag: "");
                    registrar.RegisterSingleton(instance: "service-tag", tag: "service");
                    registrar.RegisterTransient<Service<string>>(tag: "service");
                    registrar.RegisterTransient<IService, Service<string>>(tag: "service");
                });
                using var child = container.CreateChild(registrar =>
                    registrar.RegisterSingleton(instance: "child", tag: "dependency"));
                using var scope = child.CreateLocalScope();

                return new[]
                {
                    container.Resolve<Service<string>>("service").Value,
                    container.Resolve<IService>("service").Value,
                    container.For<Service<string>>().Create().Value,
                    child.Resolve<Service<string>>("service").Value,
                    child.Resolve<IService>("service").Value,
                    child.For<Service<string>>().Create().Value,
                    scope.Resolve<Service<string>>("service").Value,
                    scope.Resolve<IService>("service").Value,
                    scope.For<Service<string>>().Create().Value,
                };
            }
        }
        """;

    [TestCase("null")]
    [TestCase("(int[])null")]
    [TestCase("true")]
    [TestCase("(byte)1")]
    [TestCase("(sbyte)-1")]
    [TestCase("(short)-1")]
    [TestCase("(ushort)1")]
    [TestCase("1")]
    [TestCase("1U")]
    [TestCase("1L")]
    [TestCase("ulong.MaxValue")]
    [TestCase("int.MinValue")]
    [TestCase("long.MinValue")]
    [TestCase("1.25F")]
    [TestCase("1.25D")]
    [TestCase("float.NaN")]
    [TestCase("float.PositiveInfinity")]
    [TestCase("double.NaN")]
    [TestCase("double.NegativeInfinity")]
    [TestCase("'\\''")]
    [TestCase("'\\n'")]
    [TestCase("\"\"")]
    [TestCase("\"quoted\\\"\\\\\\n한글\"")]
    [TestCase("Tags.Prefix + nameof(Service)")]
    [TestCase("Tags.Kind.First")]
    [TestCase("Tags.Kind.First | Tags.Kind.Second")]
    [TestCase("(Tags.Kind)ulong.MaxValue")]
    [TestCase("typeof(string)")]
    [TestCase("typeof(int[])")]
    [TestCase("typeof(System.Collections.Generic.Dictionary<string, int[]>)")]
    [TestCase("typeof(System.Collections.Generic.Dictionary<,>)")]
    public void TagValue_ShouldResolveWithOriginalValueAndType(string expression)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public static class Tags
            {
                public const string Prefix = "tag-";
                public enum Kind : ulong { First = 1, Second = 2 }
            }

            public sealed class Service
            {
                public string Value { get; }

                [Inject]
                public Service([Tag(value: {{expression}})] string value) { Value = value; }
            }

            public static class Probe
            {
                public static string[] Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "tagged", tag: {{expression}});
                        registrar.RegisterTransient<Service>(tag: "service");
                    });

                    return new[]
                    {
                        container.Resolve<Service>("service").Value,
                        container.For<Service>().Create().Value,
                    };
                }
            }
            """
        );

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(new[] { "tagged", "tagged" }));
    }

    [Test]
    public void TaggedConstructor_ShouldResolveIndependentTagsInEachResolverContext()
    {
        var input = CreateCompilation(TaggedConsumer).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(TaggedConstructors, InputParseOptions, "Service.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(
            Enumerable.Repeat("tagged|default|default|default|empty", 3)
                .Concat(Enumerable.Repeat("child|default|default|default|empty", 6))));
    }

    [Test]
    public void ExternalTaggedConstructor_ShouldResolveIndependentTagsInEachResolverContext()
    {
        var external = EmitAssembly(CreateCompilation(TaggedConstructors, "ExternalServices"));
        var (compilation, _) = Generate(TaggedConsumer, additionalReferences: MetadataReference.CreateFromImage(external));

        Assert.That(Execute<string[]>(compilation, external), Is.EqualTo(
            Enumerable.Repeat("tagged|default|default|default|empty", 3)
                .Concat(Enumerable.Repeat("child|default|default|default|empty", 6))));
    }

    [Test]
    public void MissingRequiredTag_ShouldNotUseDefaultRegistration()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                [Inject]
                public Service([Tag("missing")] string value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "default");
                        registrar.RegisterTransient<Service>();
                    });

                    try { container.Resolve<Service>(); }
                    catch (System.InvalidOperationException) { return true; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void UnrelatedTagAttribute_ShouldResolveDefaultDependency()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class TagAttribute : System.Attribute { }

            public sealed class Service
            {
                public string Value { get; }

                [Inject]
                public Service([Tag] string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "default");
                        registrar.RegisterTransient<Service>();
                    });

                    return container.Resolve<Service>().Value == "default";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
