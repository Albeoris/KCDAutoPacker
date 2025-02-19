namespace KCDAutoPacker;

public static class FileUtils
{
    public static Boolean IsTempOrHiddenFile(String fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
            return true;
        return false;
    }

    public static Boolean IsOriginalFile(String fullPath)
    {
        return fullPath.IndexOf(".original", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static String GetDisplayPath(String dir, String workingDirectory)
    {
        return Path.GetRelativePath(workingDirectory, dir);
    }

    public static String? FindUnpackedFolder(String path, String workingDirectory)
    {
        if (Directory.Exists(path))
        {
            if (path.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        String? dir = Path.GetDirectoryName(path);
        while (!String.IsNullOrEmpty(dir) && dir.Length >= workingDirectory.Length)
        {
            if (dir.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}