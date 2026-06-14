using MkSidecar.Extensions;
using System.Globalization;
using System.Xml.Linq;

namespace MkSidecar.Xmp.Fragments;

internal sealed class XmpGpsFragment(double latitude, double longitude) : XmpNamedFragment<XmpGpsFragment>, IXmpNamedFragment
{
    public static string Id => "gps";

    public override void ApplyTo(XElement description)
    {
        description.SetAttributeValue(XNamespace.Exif.GetName("GPSLatitude"),
            latitude.ToString("G17", CultureInfo.InvariantCulture));
        description.SetAttributeValue(XNamespace.Exif.GetName("GPSLongitude"),
            longitude.ToString("G17", CultureInfo.InvariantCulture));
    }

    public override string ToString() => $"{Id}({latitude}, {longitude})";
}
