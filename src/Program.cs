using ConsoleAppFramework;
using ncmdump_net.src;

internal class Program
{
    static private readonly List<string> InputFiles = [];
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

        if (i < args.Length)
        {
            args = args[i..];
        }
        else
        {
            args = [];
        }
        ConsoleApp.Version = "1.0.1";
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
        public static void Process(string outputDir = "", string? inputDir = null, bool recursive = false)
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

    private static void ProcessFile(string filePath, string outputDir)
    {
        // skip if the extension is not .ncm
        if (!Path.GetExtension(filePath).Equals(".ncm", StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        // process the file
        NeteaseCloudMusic? currentFile;
        try
        {
            currentFile = new NeteaseCloudMusic(filePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Reading '{filePath}' failed: {e.Message}");
            throw new Exception("failed to process file");
        }

        try
        {
            currentFile.Dump(outputDir);
            try
            {
                currentFile.FixMetadata(true);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Warning] Fixing metadata of '{filePath}' failed: {e.Message}");
                throw new Exception("failed to process file");
            }
            Console.WriteLine($"[Done] Processed '{filePath}' to '{currentFile.GetDumpFilePath()}'");
            return;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Processing '{filePath}' failed: {e.Message}");
            throw new Exception("failed to process file");
        }
    }

    private static void ProcessDirectory(string directoryPath, string outputDir, bool recursive)
    {
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