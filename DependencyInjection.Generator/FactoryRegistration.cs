using Microsoft.CodeAnalysis;

namespace Macaron.DependencyInjection.Generator;

internal sealed record FactoryRegistration(ITypeSymbol Implementation, Location Location);
