using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.DependencyInjection.Analysis;

internal sealed class InjectionAnalysisModels(Compilation compilation)
{
    private readonly Lazy<Compilation> _metadataCompilation = new(() =>
    {
        var csharp = (CSharpCompilation)compilation;

        return csharp.WithOptions(csharp.Options.WithMetadataImportOptions(MetadataImportOptions.All));
    });
    private readonly ConcurrentDictionary<SyntaxTree, Lazy<SemanticModel>> _models = new();

    internal SemanticModel ForTypes(SemanticModel model, IEnumerable<ITypeSymbol> types)
    {
        var comparer = SymbolEqualityComparer.Default;

        if (!types.Any(type =>
        {
            return type.ContainingAssembly is { } assembly && !comparer.Equals(assembly, model.Compilation.Assembly);
        }))
        {
            return model;
        }

        // 외부 타입의 비공개 선언도 경로 결정에 필요하므로 트리마다 한 번만 다시 바인딩한다.
        return _models
            .GetOrAdd(model.SyntaxTree, tree => new Lazy<SemanticModel>(() =>
            {
                return _metadataCompilation.Value.GetSemanticModel(tree);
            }))
            .Value;
    }
}
