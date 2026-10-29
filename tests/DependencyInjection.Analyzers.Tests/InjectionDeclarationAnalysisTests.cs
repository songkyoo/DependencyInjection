using Microsoft.CodeAnalysis;
using Macaron.DependencyInjection.Analysis;

using static Macaron.DependencyInjection.Analyzers.Tests.InjectionAnalysisTestHelper;
using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class InjectionDeclarationAnalysisTests
{
    [TestCase("[Inject] public Service(int value) { }", MethodKind.Constructor, ".ctor")]
    [TestCase("[Inject] public void Initialize(int value) { }", MethodKind.Ordinary, "Initialize")]
    public void DirectDeclaration_ShouldSelectMarkedMember(string declaration, MethodKind kind, string name)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declaration}} }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member!.MethodKind, Is.EqualTo(kind));
        Assert.That(result.Member.Name, Is.EqualTo(name));
        Assert.That(result.Member.ContainingType, Is.EqualTo(type));
    }

    [TestCase("public Service() { }")]
    [TestCase("public Service(int value) { } public Service(string value) { }")]
    public void NoDirectInject_ShouldLeaveConstructorSelectionToTheCaller(string constructors)
    {
        var input = CreateInput($"public class Service {{ {constructors} }}");
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member, Is.Null);
    }

    [TestCase("[Inject] public void First() { } [Inject] public void Second() { }", "MADI0016")]
    [TestCase("[Inject] public Service() { } [Inject] public void Initialize() { }", "MADI0016")]
    [TestCase("[Inject] public Service() { } [Inject] public Service(int value) { }", "MADI0005")]
    public void MultipleDirectDeclarations_ShouldDiagnoseConflictWithRelatedLocation(string declarations, string expectedId)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declarations}} }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());
        var diagnostic = result.Diagnostics.Single();

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.Member, Is.Null);
        Assert.That(diagnostic.Id, Is.EqualTo(expectedId));
        Assert.That(diagnostic.Location.IsInSource, Is.True);
        Assert.That(diagnostic.AdditionalLocations, Has.Count.EqualTo(1));
        Assert.That(diagnostic.GetMessage(), Does.EndWith("."));
    }

    [TestCase("public void Initialize() { }")]
    [TestCase("public new void Initialize() { }")]
    [TestCase("public override void Initialize() { }")]
    public void InheritedInject_ShouldNotBecomeDerivedDeclaration(string derivedMember)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Base { [Inject] public virtual void Initialize() { } }

            public class Derived : Base { {{derivedMember}} }
            """
        );
        var baseType = input.GetTypeByMetadataName("Base")!;
        var derived = input.GetTypeByMetadataName("Derived")!;
        var baseResult = InjectionDeclarationAnalysis.Analyze(baseType, input, baseType.Locations.Single());
        var derivedResult = InjectionDeclarationAnalysis.Analyze(derived, input, derived.Locations.Single());

        AssertCompiles(input);
        Assert.That(baseResult.Member!.ContainingType, Is.EqualTo(baseType));
        Assert.That(derivedResult.HasErrors, Is.False);
        Assert.That(derivedResult.Member, Is.Null);
    }

    [TestCase("[Inject] public Derived() { }", MethodKind.Constructor)]
    [TestCase("[Inject] public override void Initialize() { }", MethodKind.Ordinary)]
    [TestCase("[Inject] public new void Initialize() { }", MethodKind.Ordinary)]
    public void DerivedDeclaration_ShouldDefineItsOwnPathRegardlessOfBase(string declaration, MethodKind kind)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Base { [Inject] public virtual void Initialize() { } }

            public class Derived : Base { {{declaration}} }
            """
        );
        var type = input.GetTypeByMetadataName("Derived")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member!.ContainingType, Is.EqualTo(type));
        Assert.That(result.Member.MethodKind, Is.EqualTo(kind));
    }

    [TestCase("[Inject] public static void Initialize() { }")]
    [TestCase("[Inject] public int Initialize() => 0;")]
    [TestCase("[Inject] public void Initialize<T>() { }")]
    [TestCase("[Inject] public async void Initialize() { await System.Threading.Tasks.Task.Yield(); }")]
    [TestCase("public int Value { get; [Inject] set; }")]
    public void UnsupportedMethod_ShouldDiagnoseUnusedDeclaration(string declaration)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{declaration}} }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0017" }));
        Assert.That(result.Member, Is.Not.Null);
    }

    [Test]
    public void BaseConstructorInjection_ShouldNotRestrictDerivedMethodDeclaration()
    {
        var input = CreateInput(
            """
            using Macaron.DependencyInjection;

            public class Base { [Inject] public Base(int value) { } }

            public class Derived : Base
            {
                public Derived() : base(1) { }

                [Inject] public void Initialize() { }
            }
            """
        );
        var type = input.GetTypeByMetadataName("Derived")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member!.MethodKind, Is.EqualTo(MethodKind.Ordinary));
        Assert.That(result.Member.ContainingType, Is.EqualTo(type));
    }

    [TestCase("Hidden.Value")]
    [TestCase("typeof(Hidden)")]
    public void InaccessibleTagValueType_ShouldDiagnoseValueExpression(string value)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                private enum Hidden { Value }

                [Inject] public void Initialize([Tag({{value}})] int number) { }
            }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());
        var diagnostic = result.Diagnostics.Single();

        AssertCompiles(input);
        Assert.That(diagnostic.Id, Is.EqualTo("MADI0013"));
        Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan), Is.EqualTo(value));
    }

    [TestCase("[Tag(\"main\")] string value", "", "MADI0012")]
    [TestCase("[Assisted] int value", "", "MADI0012")]
    [TestCase("[Tag, Assisted] int value", "[Inject]", "MADI0014")]
    [TestCase("[Tag(new[] { 1 })] string value", "[Inject]", "MADI0011")]
    [TestCase("ref int value", "[Inject]", "MADI0009")]
    [TestCase("dynamic value", "[Inject]", "MADI0002")]
    [TestCase("System.Span<int> value", "[Inject]", "MADI0009")]
    public void MethodParameters_ShouldUseSharedDeclarationRules(string parameter, string attribute, string expectedId)
    {
        var input = CreateInput(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{attribute}} public void Initialize({{parameter}}) { } }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        AssertCompiles(input);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { expectedId }));
        Assert.That(result.Diagnostics.Single().Location.IsInSource, Is.True);
    }

    [Test]
    public void SelectedMethod_ShouldRetainItsOwnTaggedAndAssistedParameters()
    {
        var input = CreateInput(
            """
            #nullable enable

            using Macaron.DependencyInjection;

            public class Base
            {
                [Inject] public virtual void Initialize([Tag("base")] int value) { }
            }

            public class Derived : Base
            {
                [Inject] public void Setup([Tag("derived")] in int? value = 3, [Assisted] string? text = null) { }
            }
            """
        );
        var type = input.GetTypeByMetadataName("Derived")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());
        var parameters = result.Member!.Parameters;

        AssertCompiles(input);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Member.Name, Is.EqualTo("Setup"));
        Assert.That(parameters[0].RefKind, Is.EqualTo(RefKind.In));
        Assert.That(parameters[0].ExplicitDefaultValue, Is.EqualTo(3));
        Assert.That(parameters[0].GetAttributes().Single().ConstructorArguments[0].Value, Is.EqualTo("derived"));
        Assert.That(parameters[1].NullableAnnotation, Is.EqualTo(NullableAnnotation.Annotated));
        Assert.That(parameters[1].GetAttributes().Single().AttributeClass!.Name, Is.EqualTo("AssistedAttribute"));
    }

    [Test]
    public void CompilerError_ShouldPreventDuplicateLibraryDiagnostics()
    {
        var input = CreateInput(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize(Unknown value) { } }
            """
        );
        var type = input.GetTypeByMetadataName("Service")!;
        var result = InjectionDeclarationAnalysis.Analyze(type, input, type.Locations.Single());

        Assert.That(result.HasCompilerErrors, Is.True);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
    }
}
