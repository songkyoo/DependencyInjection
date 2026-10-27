using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class ConstructorInjectionTests
{
    private const string InConstructors =
        """
        using Macaron.DependencyInjection;

        public interface IService { string Value { get; } }

        public sealed class Service<T> : IService
        {
            public string Value { get; }

            public Service(T first, T middle, T last)
            {
                Value = "wrong overload";
            }

            [Inject]
            public Service([Tag("first")] T first, in T middle, [Tag("last")] in T last)
            {
                Value = $"{first}|{middle}|{last}";
            }
        }
        """;

    private const string ValueConstructor =
        """
        using Macaron.DependencyInjection;

        public interface IService { string Value { get; } }

        public sealed class Service<T> : IService
        {
            public string Value { get; }

            [Inject]
            public Service([Tag("first")] T first, T middle, [Tag("last")] T last)
            {
                Value = $"{first}|{middle}|{last}";
            }
        }
        """;

    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string[] Run()
            {
                var calls = 0;
                using var container = DependencyContainerFactory.Create(registrar =>
                {
                    registrar.RegisterTransient<int>(() => ++calls, tag: "first");
                    registrar.RegisterTransient<int>(() => ++calls);
                    registrar.RegisterTransient<int>(() => ++calls, tag: "last");
                    registrar.RegisterTransient<Service<int>>();
                    registrar.RegisterTransient<IService, Service<int>>(tag: "interface");
                });

                return new[]
                {
                    container.For<Service<int>>().Create().Value,
                    container.Resolve<Service<int>>().Value,
                    container.Resolve<IService>("interface").Value,
                    calls.ToString(),
                };
            }
        }
        """;

    [Test]
    public void InParameters_ShouldSelectMarkedOverloadAndResolveEachDependencyInOrder()
    {
        var input = CreateCompilation(Consumer)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(InConstructors, InputParseOptions, "Service.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(["1|2|3", "4|5|6", "7|8|9", "9"]));
    }

    [Test]
    public void ExternalInParameters_ShouldPreserveConstructorSelectionAndResolutionOrder()
    {
        var external = EmitAssembly(CreateCompilation(InConstructors, "ExternalServices"));
        var (compilation, _) = Generate(Consumer, additionalReferences: MetadataReference.CreateFromImage(external));

        Assert.That(Execute<string[]>(compilation, external), Is.EqualTo(["1|2|3", "4|5|6", "7|8|9", "9"]));
    }

    [Test]
    public void ValueParameters_ShouldResolveEachDependencyOnceInConstructorOrder()
    {
        var input = CreateCompilation(Consumer)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(ValueConstructor, InputParseOptions, "Service.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(["1|2|3", "4|5|6", "7|8|9", "9"]));
    }

    [Test]
    public void ExternalValueParameters_ShouldPreserveResolutionOrder()
    {
        var external = EmitAssembly(CreateCompilation(ValueConstructor, "ExternalServices"));
        var (compilation, _) = Generate(Consumer, additionalReferences: MetadataReference.CreateFromImage(external));

        Assert.That(Execute<string[]>(compilation, external), Is.EqualTo(["1|2|3", "4|5|6", "7|8|9", "9"]));
    }
}
