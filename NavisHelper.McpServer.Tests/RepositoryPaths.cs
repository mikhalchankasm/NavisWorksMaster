namespace NavisHelper.McpServer.Tests;

internal static class RepositoryPaths
{
    private static string _root;

    internal static string Root => _root ??= FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
