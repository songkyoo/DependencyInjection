using static Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Macaron.DependencyInjection.Generator;

internal static class KeywordNameHelpers
{
    internal static string EscapeName(string name)
    {
        return GetKeywordKind(name) != None || GetContextualKeywordKind(name) != None
            ? "@" + name
            : name;
    }
}
