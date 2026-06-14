using MkSidecar.Xmp;
using System.Collections.Immutable;

namespace MkSidecar.Metadata.Combinators;

internal sealed class OrCombinator(params ImmutableArray<IMetadataParser> parsers) : IMetadataParser
{
    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        foreach (IMetadataParser parser in parsers)
        {
            if (parser.TryParse(context, results))
            {
                return true;
            }
        }
        return false;
    }
}
