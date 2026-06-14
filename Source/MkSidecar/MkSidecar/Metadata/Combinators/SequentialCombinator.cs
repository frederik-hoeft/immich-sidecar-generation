using MkSidecar.Xmp;
using System.Collections.Immutable;

namespace MkSidecar.Metadata.Combinators;

internal sealed class SequentialCombinator(params ImmutableArray<IMetadataParser> parsers) : IMetadataParser
{
    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        bool processedAny = false;
        foreach (IMetadataParser parser in parsers)
        {
            processedAny |= parser.TryParse(context, results);
        }
        return processedAny;
    }
}
