using System.Collections.Concurrent;
using System.Xml.Linq;

namespace MkSidecar.Extensions;

internal static class XNamespaceExtensions
{
    private static readonly ConcurrentDictionary<string, XNamespace> s_cache = new(StringComparer.Ordinal);

    extension (XNamespace)
    {
        public static XNamespace X => s_cache.GetOrAdd("adobe:ns:meta/", XNamespace.Get);

        public static XNamespace Rdf => s_cache.GetOrAdd("http://www.w3.org/1999/02/22-rdf-syntax-ns#", XNamespace.Get);

        public static XNamespace Exif => s_cache.GetOrAdd("http://ns.adobe.com/exif/1.0/", XNamespace.Get);

        public static XNamespace Xmp => s_cache.GetOrAdd("http://ns.adobe.com/xap/1.0/", XNamespace.Get);

        public static XNamespace Photoshop => s_cache.GetOrAdd("http://ns.adobe.com/photoshop/1.0/", XNamespace.Get);
    }
}
