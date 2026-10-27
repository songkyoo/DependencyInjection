using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Generator;

internal sealed record FactoryGenerationResult(
    ImmutableArray<FactoryDefinition> Definitions,
    ImmutableArray<CreationDefinition> Creations,
    ImmutableArray<Diagnostic> Diagnostics
);
