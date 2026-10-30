using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class AssistedLifetimeTests
{
    private const string ResourceDeclaration =
        """
        public class Resource : System.IDisposable
        {
            public int Disposals;
            public void Dispose() { Disposals++; }
        }
        """;

    [Test]
    public void ProvidedResourceAndCreateResult_ShouldRemainCallerOwned()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service : Resource
            {
                public Resource Supplied { get; }
                public Resource Resolved { get; }

                [Inject]
                public Service([Assisted] Resource supplied, Resource resolved)
                {
                    Supplied = supplied;
                    Resolved = resolved;
                }
            }

            public static class Probe
            {
                public static (bool, int, int, int, int, int) Run()
                {
                    var supplied = new Resource();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => new Resource());
                    });
                    using var scope = container.CreateTransientScope();
                    var service = scope.For<Service>().Create(supplied);
                    scope.Dispose();
                    container.Dispose();
                    var sameResource = ReferenceEquals(service.Supplied, supplied) && !ReferenceEquals(service.Resolved, supplied);
                    var suppliedBefore = supplied.Disposals;
                    var resultBefore = service.Disposals;
                    var dependencyDisposals = service.Resolved.Disposals;
                    supplied.Dispose();
                    service.Dispose();

                    return (sameResource, suppliedBefore, resultBefore, dependencyDisposals, supplied.Disposals, service.Disposals);
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ResourceDeclaration, InputParseOptions, "Resource.cs"));
        var (compilation, _) = Generate(input);

        var result = Execute<(
            bool SameResource,
            int SuppliedBefore,
            int ResultBefore,
            int Resolved,
            int SuppliedAfter,
            int ResultAfter
        )>(compilation);

        Assert.That(result, Is.EqualTo((true, 0, 0, 1, 1, 1)));
    }

    [Test]
    public void RootChildAndTransientScope_ShouldUseSelectedResolverWithoutPropagatingSuppliedArguments()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Nested
            {
                public string Value { get; }
                public Nested(string value) { Value = value; }
            }

            public class Service
            {
                public string Value { get; }
                public IDependencyResolver Resolver { get; }

                [Inject]
                public Service([Assisted] string value, [Tag("label")] string label, Nested nested, IDependencyResolver resolver)
                {
                    Value = $"{value}|{label}|{nested.Value}";
                    Resolver = resolver;
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "nested");
                        registrar.RegisterSingleton(instance: "root", tag: "label");
                        registrar.RegisterTransient<Nested>();
                    });
                    using var child = root.CreateScope(registrar => registrar.RegisterSingleton(instance: "child", tag: "label"));
                    using var scope = child.CreateTransientScope();
                    IDependencyResolver rootResolver = root;
                    IDependencyResolver childResolver = child;
                    IDependencyResolver scopeResolver = scope;
                    var rootService = rootResolver.For<Service>().Create("supplied");
                    var childService = childResolver.For<Service>().Create("supplied");
                    var scopeService = scopeResolver.For<Service>().Create("supplied");

                    return rootService.Value == "supplied|root|nested" && ReferenceEquals(rootService.Resolver, root)
                        && childService.Value == "supplied|child|nested" && ReferenceEquals(childService.Resolver, child)
                        && scopeService.Value == "supplied|child|nested" && ReferenceEquals(scopeService.Resolver, scope);
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ConstructorFailure_ShouldPreserveProvidedResourceAndCompletedResolveOwnership()
    {
        var input = CreateCompilation(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject]
                public Service([Assisted] Resource supplied, Resource resolved) { throw Probe.Failure; }
            }

            public static class Probe
            {
                public static readonly Exception Failure = new Exception("constructor failed");

                public static (int, int, int, int) Run()
                {
                    var supplied = new Resource();
                    var resolved = new Resource();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => resolved);
                    });
                    using var scope = container.CreateTransientScope();

                    try { scope.For<Service>().Create(supplied); throw new Exception("Creation should fail."); }
                    catch (Exception error) when (ReferenceEquals(error, Failure)) { }

                    var suppliedBefore = supplied.Disposals;
                    var resolvedBefore = resolved.Disposals;
                    scope.Dispose();

                    return (suppliedBefore, resolvedBefore, supplied.Disposals, resolved.Disposals);
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ResourceDeclaration, InputParseOptions, "Resource.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(
            Execute<(int SuppliedBefore, int ResolvedBefore, int SuppliedAfter, int ResolvedAfter)>(compilation),
            Is.EqualTo((0, 0, 0, 1))
        );
    }

    [Test]
    public void DependencyFailure_ShouldNotCleanProvidedResourceOrEarlierResolvedDependency()
    {
        var input = CreateCompilation(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject]
                public Service([Assisted] Resource supplied, Resource resolved, string missing)
                {
                    throw new ApplicationException("Constructor must not run.");
                }
            }

            public static class Probe
            {
                public static (int, int, int, int) Run()
                {
                    var supplied = new Resource();
                    var resolved = new Resource();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => resolved);
                    });
                    using var scope = container.CreateTransientScope();

                    try { scope.For<Service>().Create(supplied); throw new Exception("Resolution should fail."); }
                    catch (InvalidOperationException) { }

                    var suppliedBefore = supplied.Disposals;
                    var resolvedBefore = resolved.Disposals;
                    scope.Dispose();

                    return (suppliedBefore, resolvedBefore, supplied.Disposals, resolved.Disposals);
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ResourceDeclaration, InputParseOptions, "Resource.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(
            Execute<(int SuppliedBefore, int ResolvedBefore, int SuppliedAfter, int ResolvedAfter)>(compilation),
            Is.EqualTo((0, 0, 0, 1))
        );
    }
}
