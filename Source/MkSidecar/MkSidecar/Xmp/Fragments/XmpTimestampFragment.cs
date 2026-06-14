using MkSidecar.Extensions;
using System.Globalization;
using System.Xml.Linq;

namespace MkSidecar.Xmp.Fragments;

internal sealed class XmpTimestampFragment : XmpNamedFragment<XmpTimestampFragment>, IXmpNamedFragment
{
    private readonly DateTimeOffset _timestamp;

    public static string Id => "timestamp";

    public XmpTimestampFragment(DateTimeOffset timestamp) => _timestamp = timestamp;

    public XmpTimestampFragment(MetadataParserContext context, DateTime localDateTime)
    {
        if (context.TimeZone.IsInvalidTime(localDateTime))
        {
            throw new ArgumentException($"Invalid local time due to DST gap: {context.File.FullName}", nameof(localDateTime));
        }

        TimeSpan offset;
        if (context.TimeZone.IsAmbiguousTime(localDateTime))
        {
            TimeSpan[] offsets = context.TimeZone.GetAmbiguousTimeOffsets(localDateTime);
            // Prefer the pre-transition (larger / more-positive) offset.
            offset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
            Console.WriteLine($"WARN ambiguous local time due to DST overlap, using offset {offset}: {context.File.FullName}");
        }
        else
        {
            offset = context.TimeZone.GetUtcOffset(localDateTime);
        }
        _timestamp = new DateTimeOffset(localDateTime, offset);
    }

    public override void ApplyTo(XElement description)
    {
        string formatted = _timestamp.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        description.SetAttributeValue(XNamespace.Exif.GetName("DateTimeOriginal"), formatted);
        description.SetAttributeValue(XNamespace.Xmp.GetName("CreateDate"), formatted);
        description.SetAttributeValue(XNamespace.Xmp.GetName("MediaCreateDate"), formatted);
        description.SetAttributeValue(XNamespace.Photoshop.GetName("DateCreated"), formatted);
    }

    public override string ToString() => $"{Id}({_timestamp:yyyy-MM-ddTHH:mm:sszzz})";
}
