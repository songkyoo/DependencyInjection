using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Analysis;

internal sealed record InjectionDeclaration(
    IMethodSymbol? Member,
    ImmutableArray<Diagnostic> Diagnostics,
    bool HasCompilerErrors
)
{
    internal bool HasErrors => HasCompilerErrors || !Diagnostics.IsEmpty;
}
