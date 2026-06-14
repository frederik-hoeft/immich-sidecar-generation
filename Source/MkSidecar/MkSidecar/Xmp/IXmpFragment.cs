using System.Xml.Linq;

namespace MkSidecar.Xmp;

internal interface IXmpFragment
{
    string Id { get; }

    void ApplyTo(XElement description);

    string ToString();
}
