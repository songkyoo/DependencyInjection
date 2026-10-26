using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryTagTests
{
    [TestCase("null")]
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
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "tagged", tag: {{expression}});
                        registrar.RegisterTransient<Service>(tag: "service");
                    });

                    return container.Resolve<Service>("service").Value == "tagged";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TaggedConstructor_ShouldResolveEachParameterIndependently(bool singleType, bool externalType)
    {
        const string declaration =
            """
            using Macaron.DependencyInjection;
            using DependencyTag = Macaron.DependencyInjection.TagAttribute;

            public interface IService { }

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
        var external = externalType
            ? EmitAssembly(CreateCompilation(source: declaration, assemblyName: "ExternalServices"))
            : null;
        var typeArguments = singleType ? "Service<string>" : "IService, Service<string>";
        var serviceType = singleType ? "Service<string>" : "IService";
        var (compilation, _) = Generate(
            (externalType ? "using Macaron.DependencyInjection;" : declaration) +
            $$"""

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "default");
                        registrar.RegisterSingleton(instance: "tagged", tag: "dependency");
                        registrar.RegisterSingleton(instance: "empty", tag: "");
                        registrar.RegisterSingleton(instance: "service-tag", tag: "service");
                        registrar.RegisterTransient<{{typeArguments}}>(tag: "service");
                    });
                    using var child = container.CreateScope(registrar =>
                        registrar.RegisterSingleton(instance: "child", tag: "dependency"));
                    using var transientScope = child.CreateTransientScope();

                    var rootValue = ((Service<string>)container.Resolve<{{serviceType}}>("service")).Value;
                    var childValue = ((Service<string>)child.Resolve<{{serviceType}}>("service")).Value;
                    var scopeValue = ((Service<string>)transientScope.Resolve<{{serviceType}}>("service")).Value;

                    return rootValue == "tagged|default|default|default|empty"
                        && childValue == "child|default|default|default|empty"
                        && scopeValue == childValue;
                }
            }
            """,
            assemblyName: "TagConsumer",
            additionalReferences: external is null ? [] : [MetadataReference.CreateFromImage(external)]
        );

        Assert.That(Execute<bool>(compilation, external is null ? [] : [external]), Is.True);
    }

    [Test]
    public void MissingTag_ShouldNotUseDefaultRegistrationOrOptionalParameterValue()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                [Inject]
                public Service([Tag("missing")] string value = "optional") { }
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
