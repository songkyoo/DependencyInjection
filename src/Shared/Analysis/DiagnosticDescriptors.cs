using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Analysis;

internal static class DiagnosticDescriptors
{
    private const string Category = "Macaron.DependencyInjection";

    internal static readonly DiagnosticDescriptor ImplementationMustBeClosed = new(
        id: "MADI0001",
        title: "Injection target must be fully closed",
        messageFormat: "Injection target type '{0}' must be fully closed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DynamicTypeNotSupported = new(
        id: "MADI0002",
        title: "Dynamic is not supported for injection",
        messageFormat: "Type '{0}' contains dynamic, which is not supported for {2} on '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor InvalidImplementationType = new(
        id: "MADI0003",
        title: "Injection target type is not supported",
        messageFormat: "Injection target type '{0}' must be a concrete class or a non-ref-like struct.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ImplementationNotAccessible = new(
        id: "MADI0004",
        title: "Injection target is inaccessible to generated code",
        messageFormat: "Type '{0}' is inaccessible to generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor MultipleInjectMembers = new(
        id: "MADI0005",
        title: "Multiple members have Inject",
        messageFormat: "Type '{0}' must not have more than one constructor or method marked with Inject.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor NoAccessibleConstructor = new(
        id: "MADI0006",
        title: "No constructor is available for injection",
        messageFormat: "Type '{0}' has no constructor accessible to generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AmbiguousConstructors = new(
        id: "MADI0007",
        title: "Constructor selection is ambiguous",
        messageFormat: "Type '{0}' has multiple constructors accessible to generated code; mark one accessible constructor with Inject.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnsupportedParameter = new(
        id: "MADI0008",
        title: "Injection parameter is not supported",
        messageFormat: "Parameter '{0}' on '{1}' has an unsupported type or passing mode for {3}: '{2}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DependencyNotAccessible = new(
        id: "MADI0009",
        title: "Dependency type is inaccessible to generated code",
        messageFormat: "Dependency type '{0}' of {3} parameter '{1}' on type '{2}' is inaccessible to generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ArrayTagNotSupported = new(
        id: "MADI0010",
        title: "Tag attribute value must not be an array",
        messageFormat: "Tag attribute value for parameter '{0}' on '{1}' must not be an array.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor RequiresInject = new(
        id: "MADI0011",
        title: "Injection parameter requires Inject",
        messageFormat: "{3} '{1}' must be marked with Inject because parameter '{0}' has {2}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor TagValueTypeNotAccessible = new(
        id: "MADI0012",
        title: "Tag value type is inaccessible to generated code",
        messageFormat: "Type '{0}' used by Tag on parameter '{1}' of '{2}' is inaccessible to generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ConflictingParameterAttributes = new(
        id: "MADI0013",
        title: "Injection parameter attributes are mutually exclusive",
        messageFormat: "Parameter '{0}' on '{1}' must not have both Tag and Assisted.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AssistedTypeCannotBeRegistered = new(
        id: "MADI0014",
        title: "Assisted constructor cannot be registered",
        messageFormat: "Type '{0}' has an accessible constructor marked with Inject that requires Assisted arguments and cannot be registered.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnsupportedInjectionMethod = new(
        id: "MADI0015",
        title: "Injection method is not supported",
        messageFormat: "Method '{0}' marked with Inject must be an ordinary, synchronous, non-generic instance method returning void.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor MethodInjectedTypeCannotBeRegistered = new(
        id: "MADI0016",
        title: "Method injection target cannot be registered",
        messageFormat: "Type '{0}' has an accessible method marked with Inject and cannot be registered.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor InjectionEntryPointMismatch = new(
        id: "MADI0017",
        title: "Injection entry point does not match the selected member",
        messageFormat: "Type '{0}' uses {1}; use '{2}' instead of '{3}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor InjectionMemberNotAccessible = new(
        id: "MADI0018",
        title: "Injection member is inaccessible",
        messageFormat: "Member '{0}' marked with Inject is inaccessible to generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
