using MkSidecar.Extensions;
using MkSidecar.Xmp;
using MkSidecar.Xmp.Fragments;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MkSidecar.Metadata.Parsers;

internal sealed class TimestampFormatParser([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format) : IMetadataParser
{
    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        ReadOnlySpan<char> name = context.File.NameOnly;
        if (name.Length > format.Length)
        {
            name = name[^format.Length..];
        }
        if (DateTime.TryParseExact(name, format, provider: null, DateTimeStyles.None, out DateTime timestamp))
        {
            results.Add(new XmpTimestampFragment(context, timestamp));
            return true;
        }
        return false;
    }
}
