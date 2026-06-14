using MkSidecar.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;

namespace MkSidecar.Xmp.Fragments;

internal sealed class XmpTimestampFragment(DateTimeOffset timestamp) : XmpNamedFragment<XmpTimestampFragment>, IXmpNamedFragment
{
    public static string Id => "timestamp";

    public static bool TryCreate(MetadataParserContext context, DateTime localDateTime, [NotNullWhen(true)] out XmpTimestampFragment? fragment)
    {
        if (context.TimeZone.IsInvalidTime(localDateTime))
        {
            Console.WriteLine($"WARN invalid local time due to DST gap, ignoring timestamp candidate: {context.File.FullName}");
            fragment = null;
            return false;
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
        fragment = new XmpTimestampFragment(new DateTimeOffset(localDateTime, offset));
        return true;
    }

    public override void ApplyTo(XElement description)
    {
        string formatted = timestamp.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        description.SetAttributeValue(XNamespace.Exif.GetName("DateTimeOriginal"), formatted);
        description.SetAttributeValue(XNamespace.Xmp.GetName("CreateDate"), formatted);
        description.SetAttributeValue(XNamespace.Xmp.GetName("MediaCreateDate"), formatted);
        description.SetAttributeValue(XNamespace.Photoshop.GetName("DateCreated"), formatted);
    }

    public override string ToString() => $"{Id}({timestamp:yyyy-MM-ddTHH:mm:sszzz})";
}
