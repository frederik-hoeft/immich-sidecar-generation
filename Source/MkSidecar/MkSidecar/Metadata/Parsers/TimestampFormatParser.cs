using MkSidecar.Extensions;
using MkSidecar.Utils;
using MkSidecar.Xmp;
using MkSidecar.Xmp.Fragments;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MkSidecar.Metadata.Parsers;

internal sealed class TimestampFormatParser([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format, bool stripTrailing = true, ImmutableArray<string> allowedPrefixes = default) : IMetadataParser
{
    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        foreach (StringSegment candidate in GenerateCandidates(context))
        {
            if (DateTime.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
            {
                results.Add(new XmpTimestampFragment(context, timestamp));
                return true;
            }
        }
        return false;
    }

    private IEnumerable<StringSegment> GenerateCandidates(MetadataParserContext context)
    {
        StringSegment name = context.File.NameOnly.ToString();
        if (stripTrailing && name.Length > format.Length)
        {
            // strip off trailing characters, e.g. "2023-01-01 12-00-00 (1).jpg" -> "2023-01-01 12-00-00"
            name = name[..format.Length];
        }
        yield return name;
        if (!allowedPrefixes.IsDefaultOrEmpty)
        {
            foreach (string prefix in allowedPrefixes)
            {
                if (name.AsSpan().StartsWith(prefix))
                {
                    yield return name[prefix.Length..];
                }
            }
        }
    }
}
