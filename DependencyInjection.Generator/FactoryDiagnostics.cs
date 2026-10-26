using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Generator;

internal static class FactoryDiagnostics
{
    private const string Category = "Macaron.DependencyInjection";

    internal static readonly DiagnosticDescriptor ImplementationMustBeClosed = new(
        id: "MADI0001",
        title:"Implementation type must be fully closed",
        messageFormat: "Implementation type '{0}' must be fully closed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DynamicTypeNotSupported = new(
        id: "MADI0002",
        title: "Dynamic is not supported by automatic factories",
        messageFormat: "Type '{0}' contains dynamic, which is not supported by automatic factories for implementation '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor InvalidImplementationType = new(
        id: "MADI0003",
        title: "Implementation type cannot be constructed by an automatic factory",
        messageFormat: "Implementation type '{0}' must be a concrete class or a non-ref-like struct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ImplementationNotAccessible = new(
        id: "MADI0004",
        title: "Implementation type is inaccessible to generated code",
        messageFormat: "Implementation type '{0}' is inaccessible to generated factory code",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor MultipleInjectConstructors = new(
        id: "MADI0005",
        title: "Multiple constructors have Inject",
        messageFormat: "Implementation type '{0}' must not have more than one constructor marked with Inject",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor NoPublicConstructor = new(
        id: "MADI0006",
        title: "No constructor is available for injection",
        messageFormat: "Implementation type '{0}' has no public constructor and no constructor marked with Inject",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AmbiguousPublicConstructors = new(
        id: "MADI0007",
        title: "Constructor selection is ambiguous",
        messageFormat: "Implementation type '{0}' has multiple public constructors; mark one constructor with Inject",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ConstructorNotAccessible = new(
        id: "MADI0008",
        title: "Selected constructor is inaccessible to generated code",
        messageFormat: "Selected constructor '{0}' on implementation type '{1}' is inaccessible to generated factory code",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnsupportedConstructorParameter = new(
        id: "MADI0009",
        title: "Constructor parameter is not supported",
        messageFormat: "Constructor parameter '{0}' on implementation type '{1}' has an unsupported type or passing mode: '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DependencyNotAccessible = new(
        id: "MADI0010",
        title: "Dependency type is inaccessible to generated code",
        messageFormat: "Dependency type '{0}' of constructor parameter '{1}' on implementation type '{2}' is inaccessible to generated factory code",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ArrayTagNotSupported = new(
        id: "MADI0011",
        title: "Tag attribute value must not be an array",
        messageFormat: "Tag attribute value for parameter '{0}' on '{1}' must not be an array",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
