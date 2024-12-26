using ConsoleAppFramework;
using LibNCM;

internal class Program
{
    private static readonly List<string> InputFiles = [];
    private static void Main(string[] args)
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

        args = i < args.Length ? args[i..] : ([]);
        ConsoleApp.Version = "1.1.0";
        ConsoleApp.Run(args, Commands.Process);
    }

    static class Commands
    {
        /// <summary>
        /// Process the input files or directories.
        /// </summary>
        /// <param name="outputDir">-o, Output directory.</param>
        /// <param name="inputDir">-i|-d, Input directory.</param>
        /// <param name="recursive">-r, Recursive processing of directories.</param>
        public static void Process(string? outputDir = null, string? inputDir = null, bool recursive = false)
        {
            foreach (string inputFile in InputFiles)
            {
                Console.WriteLine($"[Info] Processing '{inputFile}'");
                try
                {
                    ProcessFile(inputFile, outputDir);
                }
                catch (Exception)
                {
                    Console.WriteLine($"[Error] Failed to process file '{inputFile}");
                }
            }
            if (inputDir is not null)
            {
                ProcessDirectory(inputDir, outputDir, recursive);
            }
        }
    }

    private static void ProcessFile(string filePath, string? outputDir)
    {
        // skip if the extension is not .ncm
        if (!Path.GetExtension(filePath).Equals(".ncm", StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        outputDir ??= Path.GetDirectoryName(filePath) ?? "./";

        // process the file
        NeteaseCloudMusicStream? currentFile = null;
        try
        {
            currentFile = new NeteaseCloudMusicStream(filePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Reading '{filePath}' failed: {e.Message}");
            currentFile?.Dispose();
            throw new Exception("Failed to read file");
        }

        try
        {
            currentFile.DumpToFile(outputDir!, fileName);
            try
            {
                currentFile.FixMetadata(true);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Warning] Fixing metadata of '{filePath}' failed: {e.Message}");
            }
            Console.WriteLine($"[Done] Processed '{filePath}' to '{outputDir}'");
            return;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Processing '{filePath}' failed: {e.Message}");
            throw new Exception("Failed to process file");
        }
        finally
        {
            currentFile?.Dispose();
        }
    }

    private static void ProcessDirectory(string directoryPath, string? outputDir, bool recursive)
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
                ProcessFile(file.FullName, outputDir);
            }
            catch (Exception)
            {
                Console.WriteLine($"[Error] Failed to process file '{file.FullName}");
            }
        }
        if (recursive)
        {
            foreach (var subdir in dir.GetDirectories())
            {
                ProcessDirectory(subdir.FullName, Path.Combine(outputDir, subdir.Name), recursive);
            }
        }
    }
}