using MkSidecar.Xmp;
using System.Collections.Immutable;

namespace MkSidecar.Metadata.Combinators;

internal sealed class AndCombinator(params ImmutableArray<IMetadataParser> parsers) : IMetadataParser
{
    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        List<IXmpFragment> tempResults = [];
        foreach (IMetadataParser parser in parsers)
        {
            if (!parser.TryParse(context, tempResults))
            {
                return false;
            }
        }
        results.AddRange(tempResults);
        return true;
    }
}
