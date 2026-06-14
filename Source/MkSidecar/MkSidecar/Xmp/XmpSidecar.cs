using MkSidecar.Extensions;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace MkSidecar.Xmp;

internal sealed class XmpSidecar(params ImmutableArray<string> requiredFragments)
{
    private readonly HashSet<string> _appliedFragments = new(StringComparer.Ordinal);
    private readonly XElement _description = new(XNamespace.Rdf + "Description", new XAttribute(XNamespace.Rdf + "about", string.Empty));

    public void Add(IXmpFragment fragment)
    {
        if (!_appliedFragments.Add(fragment.Id))
        {
            throw new InvalidOperationException($"Fragment already applied: {fragment.Id}");
        }

        fragment.ApplyTo(_description);
    }

    public XDocument CreateDocument()
    {
        foreach (string fragment in requiredFragments)
        {
            if (!_appliedFragments.Contains(fragment))
            {
                throw new InvalidOperationException($"Missing required fragment: {fragment}");
            }
        }
        return new XDocument(
            new XProcessingInstruction("xpacket", """
                begin="" id="W5M0MpCehiHzreSzNTczkc9d"
                """),
            new XElement(
                XNamespace.X + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XNamespace.X),
                new XElement(
                    XNamespace.Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", XNamespace.Rdf),
                    new XAttribute(XNamespace.Xmlns + "exif", XNamespace.Exif),
                    new XAttribute(XNamespace.Xmlns + "xmp", XNamespace.Xmp),
                    new XAttribute(XNamespace.Xmlns + "photoshop", XNamespace.Photoshop),
                    _description)),
            new XProcessingInstruction("xpacket", """
                end="w"
                """));
    }

    public async Task SaveAsync(string sidecarPath, bool overwrite, string? tempPath = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(sidecarPath) && !overwrite)
        {
            throw new IOException($"Sidecar already exists: {sidecarPath}");
        }

        string? directory = Path.GetDirectoryName(sidecarPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        Directory.CreateDirectory(directory);

        tempPath ??= Path.Combine(directory, $".{Path.GetFileName(sidecarPath)}.{Guid.NewGuid():N}.tmp");

        {
            await using FileStream tempFile = File.OpenWrite(tempPath);
            await CreateDocument().SaveAsync(tempFile, SaveOptions.None, cancellationToken);
        }

        try
        {
            File.Move(tempPath, sidecarPath, overwrite);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }
}
