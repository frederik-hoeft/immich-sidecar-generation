using System.Xml.Linq;

namespace MkSidecar.Xmp.Fragments;

internal abstract class XmpNamedFragment<T> : IXmpFragment where T : XmpNamedFragment<T>, IXmpNamedFragment
{
    string IXmpFragment.Id => T.Id;

    public abstract void ApplyTo(XElement description);

    public abstract override string ToString();
}
