using MkSidecar.Xmp;

namespace MkSidecar.Metadata;

internal interface IMetadataParser
{
    bool TryParse(MetadataParserContext context, List<IXmpFragment> results);
}
