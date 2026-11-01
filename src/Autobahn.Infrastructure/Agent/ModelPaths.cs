using Autobahn.Domain.Common;

namespace Autobahn.Infrastructure.Agent;

public static class ModelPaths
{
    public static string Resolve(string root, string relative, bool mustExist = false)
    {
        var boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(x => x is ".." or "." or ""))
            throw new DomainException("invalid_path", "Use a relative model-store path.");
        var full = Path.GetFullPath(Path.Combine(boundary, relative));
        if (!full.StartsWith(boundary + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new DomainException("invalid_path", "The path is outside the model store.");
        // Reparse points include symlinks and junctions. Check existing ancestors as well as the leaf.
        for (var item = full; !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
        {
            if ((Directory.Exists(item) || File.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new DomainException("symbolic_link", "Symbolic links are not allowed in the model store.");
            if (item == boundary) break;
        }
        if (mustExist && !Directory.Exists(full) && !File.Exists(full)) throw new DomainException("not_found", "The model path does not exist.");
        return full;
    }

    public static IEnumerable<FileInfo> Files(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateFiles(directory, "*", options).Select(x => new FileInfo(x));
    }
}
