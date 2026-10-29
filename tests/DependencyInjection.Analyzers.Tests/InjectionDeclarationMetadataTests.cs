using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Analyzers.Tests.InjectionAnalysisTestHelper;
using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class InjectionDeclarationMetadataTests
{
    [TestCase("public")]
    [TestCase("internal")]
    public void SourceAccessibleMethod_ShouldPassUseValidation(string accessibility)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] {{accessibility}} void Initialize() { } }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(InjectionDeclarationAnalysis.ValidateMethodUse(result.Member!, input, type.Locations.Single()), Is.Null);
    }

    [TestCase("private")]
    [TestCase("protected")]
    [TestCase("private protected")]
    public void SourceInaccessibleMethod_ShouldFailAtUseWithoutChangingSelection(string accessibility)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] {{accessibility}} void Initialize() { } }

            public class Use { }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);
        var diagnostic = InjectionDeclarationAnalysis.ValidateMethodUse(result.Member!, input, useLocation);

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(diagnostic!.Id, Is.EqualTo("MADI0018"));
        Assert.That(diagnostic.Location, Is.EqualTo(useLocation));
        Assert.That(diagnostic.AdditionalLocations, Has.Count.EqualTo(1));
    }

    [TestCase("private")]
    [TestCase("internal")]
    [TestCase("protected")]
    [TestCase("protected internal")]
    [TestCase("private protected")]
    public void ExternalInaccessibleMethod_ShouldRemainSelectedAndFailAtConsumerUse(string accessibility)
    {
        var image = EmitAssembly(CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] {{accessibility}} void Initialize() { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);
        var diagnostic = InjectionDeclarationAnalysis.ValidateMethodUse(result.Member!, input, useLocation);

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member!.Name, Is.EqualTo("Initialize"));
        Assert.That(diagnostic!.Id, Is.EqualTo("MADI0018"));
        Assert.That(diagnostic.Location, Is.EqualTo(useLocation));
        Assert.That(diagnostic.AdditionalLocations, Is.Empty);
    }

    [Test]
    public void ExternalInternalMethod_WithFriendAccess_ShouldPassUseValidation()
    {
        var image = EmitAssembly(CreateInput(
            """
            using Macaron.DependencyInjection;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]
            public class Service { [Inject] internal void Initialize() { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(InjectionDeclarationAnalysis.ValidateMethodUse(result.Member!, input, useLocation), Is.Null);
    }

    [Test]
    public void ExternalAsyncVoidMethod_ShouldBeRejectedFromMetadata()
    {
        var image = EmitAssembly(CreateInput(
            """
            using Macaron.DependencyInjection;
            public class Service
            {
                [Inject] public async void Initialize() { await System.Threading.Tasks.Task.Yield(); }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0017" }));
        Assert.That(result.Diagnostics.Single().Location, Is.EqualTo(useLocation));
    }

    [Test]
    public void ExternalClosedGeneric_ShouldKeepSubstitutedParametersAndDirectDeclaration()
    {
        var image = EmitAssembly(CreateInput(
            """
            #nullable enable
            using Macaron.DependencyInjection;
            public class Service<T>
            {
                [Inject] public void Initialize([Tag("value")] in T value, [Assisted] string? name = null) { }
            }
            public class Derived : Service<int> { }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service`1")!.Construct(input.GetSpecialType(SpecialType.System_Int32));
        var derived = input.GetTypeByMetadataName("Derived")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);
        var derivedResult = InjectionDeclarationAnalysis.Analyze(derived, input, useLocation);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member!.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(result.Member.Parameters[0].RefKind, Is.EqualTo(RefKind.In));
        Assert.That(result.Member.Parameters[1].NullableAnnotation, Is.EqualTo(NullableAnnotation.Annotated));
        Assert.That(result.Member.Parameters[1].HasExplicitDefaultValue, Is.True);
        Assert.That(InjectionDeclarationAnalysis.ValidateMethodUse(result.Member, input, useLocation), Is.Null);
        Assert.That(derivedResult.HasErrors, Is.False);
        Assert.That(derivedResult.Member, Is.Null);
    }

    [Test]
    public void ExternalMissingInject_ShouldDiagnoseAtUse()
    {
        var image = EmitAssembly(CreateInput(
            """
            using Macaron.DependencyInjection;

            public class Service { public void Initialize([Tag("value")] int value) { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);

        Assert.That(result.Diagnostics.Single().Id, Is.EqualTo("MADI0012"));
        Assert.That(result.Diagnostics.Single().GetMessage(), Does.StartWith("Method '"));
        Assert.That(result.Diagnostics.Single().Location, Is.EqualTo(useLocation));
    }

    [Test]
    public void ExternalConflictingDeclarations_ShouldDiagnoseAtUseWithoutSourceLocations()
    {
        var image = EmitAssembly(CreateInput(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service() { }
                [Inject] public void Initialize() { }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateConsumer(image);
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(result.Member, Is.Null);
        Assert.That(diagnostic.Id, Is.EqualTo("MADI0016"));
        Assert.That(diagnostic.Location, Is.EqualTo(useLocation));
        Assert.That(diagnostic.AdditionalLocations, Is.Empty);
    }

    [Test]
    public void ExternalInaccessibleDependency_ShouldUseMethodParameterDiagnosticAtUse()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ExternalServices"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ExternalServices");
        var dependency = module.DefineType("Dependency", TypeAttributes.NotPublic).CreateType();
        var service = module.DefineType("Service", TypeAttributes.Public);
        var method = service.DefineMethod("Initialize", MethodAttributes.Public, typeof(void), new[] { dependency });

        method.SetCustomAttribute(new CustomAttributeBuilder(typeof(InjectAttribute).GetConstructor(Type.EmptyTypes)!, []));
        method.GetILGenerator().Emit(OpCodes.Ret);
        service.CreateType();

        using var stream = new MemoryStream();

        assembly.Save(stream);

        var input = CreateCompilation(
            source: "public class Use { }",
            additionalReferences: MetadataReference.CreateFromImage(stream.ToArray())
        ).WithOptions(new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary,
            metadataImportOptions: MetadataImportOptions.All
        ));
        var type = input.GetTypeByMetadataName("Service")!;
        var useLocation = input.GetTypeByMetadataName("Use")!.Locations.Single();
        var result = InjectionDeclarationAnalysis.Analyze(type, input, useLocation);
        var diagnostic = InjectionDeclarationAnalysis.ValidateMethodUse(result.Member!, input, useLocation);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(diagnostic!.Id, Is.EqualTo("MADI0010"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("method parameter"));
        Assert.That(diagnostic.Location, Is.EqualTo(useLocation));
    }

    [Test]
    public void ProductionAnalyzerAndAttribute_ShouldKeepMethodFeatureInactive()
    {
        var input = CreateInput(
            """
            using Macaron.DependencyInjection;

            public class Service { public void Initialize([Tag("value")] int value) { } }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());
        var attributeUsage = typeof(InjectAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.That(result.Diagnostics.Single().Id, Is.EqualTo("MADI0012"));
        Assert.That(Analyze(input), Is.Empty);
        Assert.That(attributeUsage.ValidOn, Is.EqualTo(AttributeTargets.Constructor));
        Assert.That(
            new DependencyInjectionAnalyzer().SupportedDiagnostics.Select(descriptor => descriptor.Id),
            Does.Not.Contain("MADI0016").And.Not.Contain("MADI0017").And.Not.Contain("MADI0018")
        );
    }

    private static CSharpCompilation CreateConsumer(byte[] image)
    {
        var input = CreateInput("public class Use { }", additionalReferences: MetadataReference.CreateFromImage(image));

        return input.WithOptions(input.Options.WithMetadataImportOptions(MetadataImportOptions.All));
    }
}
