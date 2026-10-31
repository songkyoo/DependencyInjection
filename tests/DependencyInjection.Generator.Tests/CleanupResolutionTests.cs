using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CleanupResolutionTests
{
    private const string Types =
        """
        using System;
        using Macaron.DependencyInjection;

        public class Resource
        {
            public static int Creations;

            public Resource() { Creations++; }
        }

        public class RequiredCreation
        {
            public static int Calls;

            public RequiredCreation(Resource dependency) { Calls++; }
        }

        public class RequiredInjection
        {
            public bool Called;

            [Inject]
            public void Initialize(Resource dependency) { Called = true; }
        }

        public class OptionalCreation
        {
            public OptionalCreation(Resource dependency = null) { }
        }

        public class OptionalInjection
        {
            [Inject]
            public void Initialize(Resource dependency = null) { }
        }
        """;

    [TestCase("resolver.For<RequiredCreation>().Create();")]
    [TestCase("resolver.For<RequiredInjection>().Inject(target);")]
    public void GeneratedCall_ShouldRejectResolveDuringCleanup_BeforeCallingTarget(string statement)
    {
        var (compilation, _) = Generate(Types + "\n" +
            $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    IDependencyResolver resolver = null;
                    var target = new RequiredInjection();
                    var rejected = false;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => new Resource());
                        registrar.RegisterTransient<object>(() => new object(), cleanup: _ =>
                        {
                            try
                            {
                                {{statement}}
                            }
                            catch (InvalidOperationException)
                            {
                                rejected = true;
                            }
                        });
                    });
                    using var other = container.CreateLocalScope();
                    resolver = other;
                    using var scope = container.CreateLocalScope();
                    scope.Resolve<object>();

                    scope.Dispose();

                    return rejected && Resource.Creations == 0
                        && RequiredCreation.Calls == 0 && !target.Called;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("resolver.For<OptionalCreation>().Create();")]
    [TestCase("resolver.For<OptionalInjection>().Inject(target);")]
    public void GeneratedCall_ShouldAllowMissingOptionalDependencyDuringCleanup_WithoutResolve(string statement)
    {
        var (compilation, _) = Generate(Types + "\n" +
            $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    IDependencyResolver resolver = null;
                    var target = new OptionalInjection();
                    var completed = false;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<object>(() => new object(), cleanup: _ =>
                        {
                            {{statement}}
                            completed = true;
                        });
                    });
                    resolver = container;
                    using var scope = container.CreateLocalScope();
                    scope.Resolve<object>();

                    scope.Dispose();

                    return completed && Resource.Creations == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
