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
            if (DateTime.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp)
                && XmpTimestampFragment.TryCreate(context, timestamp, out XmpTimestampFragment? fragment))
            {
                results.Add(fragment);
                return true;
            }
        }
        return false;
    }

    private IEnumerable<StringSegment> GenerateCandidates(MetadataParserContext context)
    {
        StringSegment name = context.File.NameOnly.ToString();
        yield return Normalize(name);
        if (!allowedPrefixes.IsDefaultOrEmpty)
        {
            foreach (string prefix in allowedPrefixes)
            {
                if (name.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return Normalize(name[prefix.Length..]);
                }
            }
        }
    }

    private StringSegment Normalize(StringSegment segment)
    {
        if (stripTrailing && segment.Length > format.Length)
        {
            // strip off trailing characters, e.g. "2023-01-01 12-00-00 (1).jpg" -> "2023-01-01 12-00-00"
            return segment[..format.Length];
        }
        return segment;
    }
}
