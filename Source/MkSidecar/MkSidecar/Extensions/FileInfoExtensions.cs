namespace MkSidecar.Extensions;

internal static class FileInfoExtensions
{
    extension (FileInfo self)
    {
        public ReadOnlySpan<char> NameOnly => self.Name.AsSpan(0, self.Name.Length - self.Extension.Length);

        public ReadOnlySpan<char> ExtensionOnly => self.Extension is ['.', ..] ext ? ext.AsSpan(1) : [];
    }
}
