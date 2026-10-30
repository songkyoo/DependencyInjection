using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class OptionalDefaultValueTests
{
    [TestCase("string?", "null", "value is null")]
    [TestCase("string?", "\"guest\"", "value == \"guest\"")]
    [TestCase("char", "'\\n'", "value == '\\n'")]
    [TestCase("byte", "255", "value == 255")]
    [TestCase("short", "-3", "value == -3")]
    [TestCase("uint", "4U", "value == 4U")]
    [TestCase("long", "long.MinValue", "value == long.MinValue")]
    [TestCase("decimal", "1.25M", "value == 1.25M")]
    [TestCase("decimal?", "1.25M", "value == 1.25M")]
    [TestCase("float", "float.NaN", "float.IsNaN(value)")]
    [TestCase("double", "double.PositiveInfinity", "double.IsPositiveInfinity(value)")]
    [TestCase("double?", "double.NegativeInfinity", "value == double.NegativeInfinity")]
    [TestCase("float", "-0F", "BitConverter.SingleToInt32Bits(value) == int.MinValue")]
    [TestCase("double", "-0D", "BitConverter.DoubleToInt64Bits(value) == long.MinValue")]
    [TestCase("Mode", "Mode.Second", "value == Mode.Second")]
    [TestCase("Mode?", "Mode.Second", "value == Mode.Second")]
    [TestCase("Mode?", "null", "value is null")]
    [TestCase("DateTime", "default", "value == default(DateTime)")]
    [TestCase("Number", "default", "value.Value == 0")]
    [TestCase("Number?", "default", "value is null")]
    [TestCase("object?", "null", "value is null")]
    public void MissingDependency_ShouldPreserveDefaultValueAndTargetType(
        string type,
        string defaultExpression,
        string assertion
    )
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable
            using System;
            using Macaron.DependencyInjection;

            public enum Mode : byte { First = 1, Second = 2 }
            public struct Number
            {
                public int Value;
            }

            public sealed class Service
            {
                public bool Matches { get; }

                public Service({{type}} value = {{defaultExpression}})
                {
                    Matches = {{assertion}};
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service>());

                    return container.Resolve<Service>().Matches && container.For<Service>().Create().Matches;
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
}
