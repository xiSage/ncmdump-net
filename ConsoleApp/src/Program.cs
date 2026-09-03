using ConsoleAppFramework;
using LibNCM;

internal class Program
{
    private static readonly List<string> InputFiles = [];
    private static async Task Main(string[] args)
    {
        var i = 0;
        for (; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                InputFiles.Add(args[i].Trim('\'', '"'));
            }
            else
            {
                break;
            }
        }

        args = i < args.Length ? args[i..] : [];
        ConsoleApp.Version = "2.0.0";
        await ConsoleApp.RunAsync(args, Commands.ProcessAsync);
    }

    static class Commands
    {
        /// <summary>
        /// Process the input files or directories.
        /// </summary>
        /// <param name="outputDir">-o, Output directory.</param>
        /// <param name="inputDir">-i|-d, Input directory.</param>
        /// <param name="recursive">-r, Recursive processing of directories.</param>
        public static async Task ProcessAsync(string? outputDir = null, string? inputDir = null, bool recursive = false)
        {
            foreach (string inputFile in InputFiles)
            {
                Console.WriteLine($"[Info] Processing '{inputFile}'");
                try
                {
                    await ProcessFileAsync(inputFile, outputDir);
                }
                catch (Exception)
                {
                    Console.WriteLine($"[Error] Failed to process file '{inputFile}'");
                }
            }
            if (inputDir is not null)
            {
                await ProcessDirectoryAsync(inputDir, outputDir, recursive);
            }
        }
    }

    private static async Task ProcessFileAsync(string filePath, string? outputDir)
    {
        // skip if the extension is not .ncm
        if (!Path.GetExtension(filePath).Equals(".ncm", StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        outputDir ??= Path.GetDirectoryName(filePath) ?? "./";

        try
        {
            await using var ncm = NcmFile.Open(filePath);
            await ncm.DumpToFileAsync(outputDir, fileName);
            try
            {
                await ncm.FixMetadataAsync(fetchCoverArt: true);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Warning] Fixing metadata of '{filePath}' failed: {e.Message}");
            }
            Console.WriteLine($"[Done] Processed '{filePath}' to '{outputDir}'");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Processing '{filePath}' failed: {e.Message}");
            throw;
        }
    }

    private static async Task ProcessDirectoryAsync(string directoryPath, string? outputDir, bool recursive)
    {
        outputDir ??= directoryPath;
        var dir = new DirectoryInfo(directoryPath);
        if (!dir.Exists)
        {
            Console.WriteLine($"[Error] Directory '{directoryPath}' does not exist");
            return;
        }
        foreach (var file in dir.GetFiles("*.ncm"))
        {
            Console.WriteLine($"[Info] Processing '{file.FullName}'");
            try
            {
                await ProcessFileAsync(file.FullName, outputDir);
            }
            catch (Exception)
            {
                Console.WriteLine($"[Error] Failed to process file '{file.FullName}'");
            }
        }
        if (recursive)
        {
            foreach (var subdir in dir.GetDirectories())
            {
                await ProcessDirectoryAsync(subdir.FullName, Path.Combine(outputDir, subdir.Name), recursive);
            }
        }
    }
}