using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryInParameterTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void InParameter_ShouldSelectMarkedOverloadAndResolveInOrderOnce(bool singleType, bool tagged, bool externalType)
    {
        var declaration =
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public sealed class Service<T> : IService
            {
                public string Value { get; }

                public Service(T first, T middle, T last) { Value = "wrong overload"; }

                [Inject]
                public Service([Tag("first")] T first, {{(tagged ? "[Tag(\"middle\")]" : "")}} in T middle, [Tag("last")] T last)
                {
                    Value = $"{first}|{middle}|{last}";
                }
            }
            """;
        var external = externalType ? EmitAssembly(CreateCompilation(declaration, "ExternalServices")) : null;
        var typeArguments = singleType ? "Service<int>" : "IService, Service<int>";
        var serviceType = singleType ? "Service<int>" : "IService";
        var (compilation, _) = Generate(
            (externalType ? "using Macaron.DependencyInjection;" : declaration) +
            $$"""

            public static class Probe
            {
                public static bool Run()
                {
                    var calls = 0;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<int>(() => ++calls, tag: "first");
                        registrar.RegisterTransient<int>(() => ++calls, tag: {{(tagged ? "\"middle\"" : "null")}});
                        registrar.RegisterTransient<int>(() => ++calls, tag: "last");
                        registrar.RegisterTransient<{{typeArguments}}>();
                    });

                    return ((Service<int>)container.Resolve<{{serviceType}}>()).Value == "1|2|3" && calls == 3;
                }
            }
            """, additionalReferences: external is null ? [] : [MetadataReference.CreateFromImage(external)]
        );

        Assert.That(Execute<bool>(compilation, external is null ? [] : [external]), Is.True);
    }
}
