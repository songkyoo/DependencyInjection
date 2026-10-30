using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class OptionalDependencyFailureTests
{
    [TestCase("root")]
    [TestCase("child")]
    [TestCase("scope")]
    public void RegisteredFactoryFailure_ShouldPropagateAndKeepCompletedResolvesInTheirScope(string resolverName)
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Macaron.DependencyInjection;

            public sealed class Resource : IDisposable
            {
                public int Disposals { get; private set; }
                public void Dispose() { Disposals++; }
            }

            public sealed class Broken { }

            public sealed class Service
            {
                public static int Created;
                public Service(Resource? resource = null, Broken? broken = null) { Created++; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var resources = new List<Resource>();
                    var failure = new InvalidOperationException("factory failure");
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() =>
                        {
                            var resource = new Resource();
                            resources.Add(resource);
                            return resource;
                        });
                        registrar.RegisterTransient<Broken>(() => throw failure);
                        registrar.RegisterTransient<Service>();
                    });
                    using var child = root.CreateScope();
                    using var scope = child.CreateTransientScope();
                    IDependencyResolver resolver = {{resolverName}};

                    try { resolver.Resolve<Service>(); return false; }
                    catch (InvalidOperationException error) when (ReferenceEquals(error, failure)) { }

                    try { resolver.For<Service>().Create(); return false; }
                    catch (InvalidOperationException error) when (ReferenceEquals(error, failure)) { }

                    if (resources.Count != 2 || resources.Any(resource => resource.Disposals != 0) || Service.Created != 0)
                        return false;

                    ((IDisposable)resolver).Dispose();
                    return resources.All(resource => resource.Disposals == 1);
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void RegisteredNullFactoryResult_ShouldThrowInsteadOfSupplyingNull()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Dependency { }
            public sealed class Service
            {
                public Service(Dependency? dependency = null) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Dependency>(() => null!);
                        registrar.RegisterTransient<Service>();
                    });

                    try { container.Resolve<Service>(); return false; }
                    catch (InvalidOperationException error) when (error.Message.Contains("Factory returned null")) { }

                    try { container.For<Service>().Create(); return false; }
                    catch (InvalidOperationException error) when (error.Message.Contains("Factory returned null")) { }

                    return true;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void RegisteredDependencyWithMissingChild_ShouldPreserveMissingChildError()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Missing { }
            public sealed class Dependency
            {
                public Dependency(Missing missing) { }
            }
            public sealed class Service
            {
                public Service(Dependency? dependency = null) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Dependency>());

                    try { container.For<Service>().Create(); return false; }
                    catch (InvalidOperationException error)
                    {
                        return error.Message.Contains("Service not found") && error.Message.Contains("Missing");
                    }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void RegisteredCycleThroughNullableParameter_ShouldKeepCycleDetection()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class First
            {
                public First(Second? second = null) { }
            }
            public sealed class Second
            {
                public Second(First? first = null) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<First>();
                        registrar.RegisterTransient<Second>();
                    });

                    try { container.For<First>().Create(); return false; }
                    catch (InvalidOperationException error)
                    {
                        return error.Message.Contains("Cyclic dependency detected");
                    }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void DisposedResolver_ShouldThrowBeforeSupplyingDefaults()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public Service(string? value = "fallback") { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var container = DependencyContainerFactory.Create(_ => { });
                    var context = container.For<Service>();
                    container.Dispose();

                    try { context.Create(); return false; }
                    catch (ObjectDisposedException) { return true; }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
