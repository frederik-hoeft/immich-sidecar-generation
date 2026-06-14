using MkSidecar.Extensions;
using MkSidecar.Xmp;
using System.Collections.Frozen;

namespace MkSidecar.Metadata.Parsers;

internal sealed class ExtensionParser(params ReadOnlySpan<string> allowedExtensions) : IMetadataParser
{
    private readonly FrozenSet<string> _allowedExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, allowedExtensions);

    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results) => _allowedExtensions.Contains(context.File.Extension);
}
