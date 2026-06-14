using System.Runtime.CompilerServices;

namespace MkSidecar.Extensions;

internal static class DirectoryExtensions
{
    extension (Directory)
    {
        public static async IAsyncEnumerable<FileInfo> EnumerateFilesSafelyAsync(string root, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Stack<string> pending = [];
            pending.Push(Path.GetFullPath(root));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"SKIP directory files: {directory}: {ex.Message}");
                    continue;
                }

                foreach (string file in files)
                {
                    yield return new FileInfo(file);
                }

                IEnumerable<string> subdirectories;
                try
                {
                    subdirectories = Directory.EnumerateDirectories(directory);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"SKIP directory children: {directory}: {ex.Message}");
                    continue;
                }

                foreach (string subdirectory in subdirectories)
                {
                    FileAttributes attributes;

                    try
                    {
                        attributes = File.GetAttributes(subdirectory);
                    }
                    catch (Exception ex)
                    {
                        await Console.Error.WriteLineAsync($"SKIP directory attributes: {subdirectory}: {ex.Message}");
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Console.WriteLine($"SKIP reparse point/symlink: {subdirectory}");
                        continue;
                    }

                    pending.Push(subdirectory);
                }
            }
        }
    }
}
