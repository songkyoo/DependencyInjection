using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class AssistedGenerationTests
{
    private const string MixedConstructor =
        """
        using Macaron.DependencyInjection;

        public sealed class MarkerAttribute : System.Attribute { }

        public sealed class Service<T>
        {
            public string Value { get; }

            public Service(T first, T context, T middle, T resolver, T dependency0, string @event)
            {
                Value = "wrong overload";
            }

            [Inject]
            public Service(
                [Tag("first")] in T first,
                [Assisted, Marker] T context,
                [Tag("middle")] T middle,
                [Assisted] in T resolver,
                [Assisted] T dependency0,
                [Assisted] string @event)
            {
                Value = $"{first}|{context}|{middle}|{resolver}|{dependency0}|{@event}";
            }
        }
        """;

    private const string MixedConsumer =
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
                    registrar.RegisterTransient<int>(() => ++calls, tag: "middle");
                });
                var supplied = 20;
                var first = container.For<Service<int>>().Create(context: 10, resolver: in supplied, dependency0: 30, @event: "explicit");
                var second = container.For<Service<int>>().Create(11, 21, 31, "implicit");

                return new[] { first.Value, second.Value, calls.ToString() };
            }
        }
        """;

    [Test]
    public void MixedParameters_ShouldPreservePositionsInCallsAndAvoidGeneratedNameCollisions()
    {
        var input = CreateCompilation(MixedConsumer)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(MixedConstructor, InputParseOptions, "Service.cs"));
        var (compilation, sources) = Generate(input);

        Assert.That(Execute<string[]>(compilation), Is.EqualTo(new[] { "1|10|2|20|30|explicit", "3|11|4|21|31|implicit", "4" }));

        var create = compilation
            .GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")!
            .GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Single();
        var parameterNames = create.Parameters.Skip(1).Select(parameter => parameter.Name);
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(parameterNames, Is.EqualTo(["context", "resolver", "dependency0", "event"]));
        Assert.That(create.Parameters[2].RefKind, Is.EqualTo(RefKind.In));
        Assert.That(create.Parameters.SelectMany(parameter => parameter.GetAttributes()), Is.Empty);
        Assert.That(factories, Does.Not.Contain("typeof(global::Service<int>)"));
    }

    [Test]
    public void ExternalClosedConstructor_ShouldPreserveAssistedAndDiArgumentOrder()
    {
        var external = EmitAssembly(CreateCompilation(MixedConstructor, "ExternalServices"));
        var (compilation, _) = Generate(MixedConsumer, additionalReferences: MetadataReference.CreateFromImage(external));

        Assert.That(Execute<string[]>(compilation, external), Is.EqualTo(new[] { "1|10|2|20|30|explicit", "3|11|4|21|31|implicit", "4" }));
    }

    [Test]
    public void OptionalAndParamsArguments_ShouldPreserveSignatureAndStillCreateNewResults()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public string Value { get; }

                [Inject]
                public Service(
                    [Assisted] string? text = null,
                    [Assisted] decimal scale = 1.25M,
                    [Assisted] DayOfWeek day = DayOfWeek.Friday,
                    [Assisted] params string[] values)
                {
                    Value = $"{text ?? "null"}|{scale}|{day}|{values.Length}";
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var first = container.For<Service>().Create();
                    var second = container.For<Service>().Create();
                    var supplied = container.For<Service>().Create(
                        day: DayOfWeek.Monday,
                        text: "named",
                        scale: 2M,
                        values: new[] { "a", "b" }
                    );
                    var expanded = container.For<Service>().Create("params", 3M, DayOfWeek.Tuesday, "a", "b", "c");

                    return !ReferenceEquals(first, second) && first.Value == "null|1.25|Friday|0"
                        && supplied.Value == "named|2|Monday|2" && expanded.Value == "params|3|Tuesday|3";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);

        var create = compilation
            .GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")!
            .GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.That(create.Parameters[1].Type.NullableAnnotation, Is.EqualTo(NullableAnnotation.Annotated));
        Assert.That(create.Parameters[1].HasExplicitDefaultValue, Is.True);
        Assert.That(create.Parameters[1].ExplicitDefaultValue, Is.Null);
        Assert.That(create.Parameters.Last().IsParams, Is.True);
    }

    [Test]
    public void ExternalOptionalTupleParams_ShouldPreserveClosedNullableTypes()
    {
        const string declaration =
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                public T? Value { get; }
                public int Count { get; }
                [Inject]
                public Service([Assisted] T? value = default, [Assisted] params (int Id, string? Name)[] rows)
                {
                    Value = value;
                    Count = rows.Length;
                }
            }
            """;
        var external = EmitAssembly(CreateCompilation(declaration, "ExternalServices"));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var empty = container.For<Service<string>>().Create();
                    var supplied = container.For<Service<string>>().Create("provided", (1, null), (2, "second"));

                    return empty.Value == null && empty.Count == 0 && supplied.Value == "provided" && supplied.Count == 2;
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<bool>(compilation, external), Is.True);

        var create = compilation
            .GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")!
            .GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.That(create.Parameters[1].Type.NullableAnnotation, Is.EqualTo(NullableAnnotation.Annotated));
        Assert.That(create.Parameters[2].IsParams, Is.True);
    }

    [Test]
    public void NullableClosedType_ShouldPreserveAssistedAnnotationsWithoutCompilerWarnings()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                public T Value { get; }
                [Inject] public Service([Assisted] T value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    Service<string?> service = container.For<Service<string?>>().Create(null);
                    return service.Value == null;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            Is.Empty
        );
    }

    [Test]
    public void ClosedStruct_ShouldUseAssistedConstructorInsteadOfImplicitDefault()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public struct Service<T>
            {
                public T Value { get; }
                [Inject] public Service([Assisted] T value) { Value = value; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    return container.For<Service<int>>().Create(42).Value;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }

    [TestCase("decimal", "1.25M", "1.25M")]
    [TestCase("float", "1.25F", "1.25F")]
    [TestCase("double", "1.25D", "1.25D")]
    [TestCase("float", "float.NaN", "float.NaN")]
    [TestCase("float", "float.PositiveInfinity", "float.PositiveInfinity")]
    [TestCase("float", "float.NegativeInfinity", "float.NegativeInfinity")]
    [TestCase("double", "double.NaN", "double.NaN")]
    [TestCase("double", "double.PositiveInfinity", "double.PositiveInfinity")]
    [TestCase("double", "double.NegativeInfinity", "double.NegativeInfinity")]
    [TestCase("ulong", "ulong.MaxValue", "18446744073709551615")]
    [TestCase("int", "int.MinValue", "-2147483648")]
    [TestCase("byte", "1", "1")]
    [TestCase("char", "'\\''", "'\\''")]
    [TestCase("string", "\"quoted\\\"\\n\"", "\"quoted\\\"\\n\"")]
    [TestCase("string?", "default", "null")]
    [TestCase("int?", "null", "null")]
    [TestCase("System.DateTime", "default", "default")]
    [TestCase("(int Count, string Text)", "default", "default")]
    [TestCase("System.DayOfWeek?", "System.DayOfWeek.Friday", "global::System.DayOfWeek.Friday")]
    [SetCulture("fr-FR")]
    public void OptionalArgument_ShouldRenderReadableDefaultAndPreserveItsValue(string type, string value, string expectedExpression)
    {
        var (compilation, sources) = Generate(
            $$"""
            #nullable enable
            using Macaron.DependencyInjection;

            public class Service
            {
                public {{type}} Value { get; }

                [Inject]
                public Service([Assisted] {{type}} value = {{value}}) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var service = container.For<Service>().Create();

                    return System.Collections.Generic.EqualityComparer<{{type}}>.Default.Equals(service.Value, {{value}});
                }
            }
            """
        );

        var parameter = sources
            .Single(source => source.HintName.StartsWith("InjectionContextExtensions."))
            .SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ParameterSyntax>()
            .Single(parameter => parameter.Identifier.ValueText == "value");
        var original = compilation.GetTypeByMetadataName("Service")!.InstanceConstructors.Single().Parameters.Single();
        var generated = compilation
            .GetTypeByMetadataName("Macaron.DependencyInjection.InjectionContextExtensions")!
            .GetMembers("Create").OfType<IMethodSymbol>().Single().Parameters[1];

        Assert.That(parameter.Default!.Value.ToString(), Is.EqualTo(expectedExpression));
        Assert.That(generated.HasExplicitDefaultValue, Is.True);
        Assert.That(generated.ExplicitDefaultValue, Is.EqualTo(original.ExplicitDefaultValue));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("float", "-0.0F", "System.BitConverter.SingleToInt32Bits", "int.MinValue")]
    [TestCase("double", "-0.0D", "System.BitConverter.DoubleToInt64Bits", "long.MinValue")]
    public void NegativeZeroDefault_ShouldPreserveItsSign(string type, string value, string getBits, string expectedBits)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public {{type}} Value { get; }
                [Inject] public Service([Assisted] {{type}} value = {{value}}) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    return {{getBits}}(container.For<Service>().Create().Value) == {{expectedBits}};
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("Create()")]
    [TestCase("Create(1, 2)")]
    [TestCase("Create(\"wrong\")")]
    [TestCase("Create(unknown: 1)")]
    public void InvalidSuppliedArguments_ShouldBeRejectedByCompiler(string call)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service([Assisted] int value) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Service>().{{call}};
                }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void DefaultContext_ShouldFailBeforeAcceptingOptionalAssistedArguments()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service([Assisted] int value = 42) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    Func<IDependencyResolver, InjectionContext<Service>> forService = IDependencyResolverExtensions.For<Service>;
                    try { default(InjectionContext<Service>).Create(); }
                    catch (InvalidOperationException) { return true; }
                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
