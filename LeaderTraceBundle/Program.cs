using System.Globalization;
using System.Text.Json;
using LeaderDecoder.Services;

namespace LeaderTraceBundle;

internal static class Program
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    private static readonly HashSet<string> LogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".log", ".txt", ".json"
    };

    private static int Main(string[] args)
    {
        var diag = new DiagnosticService();

        try
        {
            if (!TryParseOptions(args, out var options, out string? error))
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
                PrintUsage();
                return 1;
            }

            if (options.Help)
            {
                PrintUsage();
                return 0;
            }

            string repoRoot = FindRepoRoot();
            string outputRoot = ResolvePath(repoRoot, options.OutputDir ?? "trace-bundles");
            string bundleName = SanitizeName(options.Name ?? "trace");
            string bundleDir = Path.Combine(outputRoot, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{bundleName}");
            string artifactsDir = Path.Combine(bundleDir, "artifacts");
            Directory.CreateDirectory(bundleDir);
            Directory.CreateDirectory(artifactsDir);

            var inventory = BuildWindowInventory();
            var (explicitFiles, logFiles, imageFiles) = CollectCandidates(repoRoot, options.MaxImages);

            var copied = new List<ArtifactRecord>();
            var missing = new List<ArtifactRecord>();
            var skipped = new List<ArtifactRecord>();

            CopyFiles(repoRoot, artifactsDir, explicitFiles, "setting", copied, missing);
            CopyFiles(repoRoot, artifactsDir, logFiles, "log", copied, missing);
            CopyImages(repoRoot, artifactsDir, imageFiles, options.MaxImages, copied, missing, skipped);

            var manifest = new
            {
                CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RepoRoot = repoRoot,
                OutputRoot = outputRoot,
                BundleDirectory = bundleDir,
                BundleName = bundleName,
                MaxImages = options.MaxImages,
                WindowInventory = inventory,
                IncludedArtifacts = copied,
                MissingArtifacts = missing,
                SkippedArtifacts = skipped,
                TotalImageCandidates = imageFiles.Count,
                CopiedImageCount = copied.Count(item => item.Kind == "image"),
            };

            string manifestPath = Path.Combine(bundleDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            string inventoryPath = Path.Combine(bundleDir, "window_inventory.json");
            File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Trace Bundle");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Repo root:        {repoRoot}");
            Console.WriteLine($"Output directory:  {bundleDir}");
            Console.WriteLine($"Window inventory:  {inventory.Count} item(s)");
            Console.WriteLine($"Artifacts copied:  {copied.Count}");
            Console.WriteLine($"Missing artifacts: {missing.Count}");
            Console.WriteLine($"Images copied:     {copied.Count(item => item.Kind == "image")}/{imageFiles.Count}");
            Console.WriteLine($"Manifest:          {manifestPath}");
            Console.WriteLine($"Inventory:         {inventoryPath}");

            return 0;
        }
        catch (Exception ex)
        {
            diag.LogToolFailure(
                source: "LeaderTraceBundle",
                operation: "UnhandledException",
                detail: "Trace bundle creation crashed.",
                context: string.Join(" ", args),
                ex: ex,
                dedupeKey: "trace-bundle-unhandled",
                throttleSeconds: 1.0);
            Console.Error.WriteLine($"Unhandled error: {ex.Message}");
            return 1;
        }
    }

    private static (List<string> explicitFiles, List<string> logFiles, List<string> imageFiles) CollectCandidates(string repoRoot, int maxImages)
    {
        var explicitFiles = new List<string>();
        var logFiles = new List<string>();
        var imageFiles = new List<string>();
        string? savedRoot = FindSavedInterfaceRoot(repoRoot);

        foreach (string relativePath in new[]
        {
            Path.Combine("LeaderDecoder", "settings.json"),
            Path.Combine("LeaderDecoder", "build_out.txt"),
            Path.Combine("LeaderDecoder", "build_output.txt"),
            Path.Combine("debug", "launcher_failures.csv"),
        })
        {
            string path = ResolvePath(repoRoot, relativePath);
            if (File.Exists(path))
            {
                explicitFiles.Add(path);
            }
        }

        if (!string.IsNullOrWhiteSpace(savedRoot) && Directory.Exists(savedRoot))
        {
            foreach (string file in EnumerateSavedAddonFiles(savedRoot))
            {
                explicitFiles.Add(file);
            }
        }

        foreach (string relativeDirectory in new[]
        {
            "debug",
            "debug-live",
            Path.Combine("LeaderDecoder", "debug"),
            Path.Combine("LeaderInputVerifier", "debug"),
            Path.Combine("LeaderInputProbe", "debug"),
            Path.Combine("LeaderLiveInspector", "debug"),
            Path.Combine("LeaderScreenshotInspector", "debug"),
            Path.Combine("LeaderWindowResizer", "debug"),
            Path.Combine("LeaderTraceBundle", "debug"),
        })
        {
            string directory = ResolvePath(repoRoot, relativeDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (ImageExtensions.Contains(extension))
                {
                    imageFiles.Add(file);
                }
                else if (LogExtensions.Contains(extension))
                {
                    logFiles.Add(file);
                }
            }
        }

        imageFiles = imageFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maxImages))
            .ToList();

        logFiles = logFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        return (explicitFiles, logFiles, imageFiles);
    }

    private static IEnumerable<string> EnumerateSavedAddonFiles(string savedRoot)
    {
        foreach (string addonSettings in Directory.EnumerateFiles(savedRoot, "AddonSettings.lua", SearchOption.AllDirectories))
        {
            yield return addonSettings;
        }

        foreach (string leaderSavedVariables in Directory.EnumerateFiles(savedRoot, "Leader.lua", SearchOption.AllDirectories)
            .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}SavedVariables{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return leaderSavedVariables;
        }
    }

    private static void CopyFiles(
        string repoRoot,
        string artifactsDir,
        IEnumerable<string> files,
        string kind,
        List<ArtifactRecord> copied,
        List<ArtifactRecord> missing)
    {
        foreach (string sourcePath in files)
        {
            CopyArtifact(repoRoot, artifactsDir, sourcePath, kind, copied, missing);
        }
    }

    private static void CopyImages(
        string repoRoot,
        string artifactsDir,
        IEnumerable<string> files,
        int maxImages,
        List<ArtifactRecord> copied,
        List<ArtifactRecord> missing,
        List<ArtifactRecord> skipped)
    {
        int copiedImages = 0;
        foreach (string sourcePath in files)
        {
            if (maxImages >= 0 && copiedImages >= maxImages)
            {
                skipped.Add(new ArtifactRecord
                {
                    Kind = "image",
                    SourcePath = sourcePath,
                    BundlePath = string.Empty,
                    Exists = true,
                    Reason = "image_limit_reached",
                });
                continue;
            }

            if (CopyArtifact(repoRoot, artifactsDir, sourcePath, "image", copied, missing))
            {
                copiedImages++;
            }
        }
    }

    private static bool CopyArtifact(
        string repoRoot,
        string artifactsDir,
        string sourcePath,
        string kind,
        List<ArtifactRecord> copied,
        List<ArtifactRecord> missing)
    {
        if (!File.Exists(sourcePath))
        {
            missing.Add(new ArtifactRecord
            {
                Kind = kind,
                SourcePath = sourcePath,
                BundlePath = string.Empty,
                Exists = false,
                Reason = "missing",
            });
            return false;
        }

        string relativePath = BuildBundleRelativePath(repoRoot, sourcePath);
        string destinationPath = Path.Combine(artifactsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        copied.Add(new ArtifactRecord
        {
            Kind = kind,
            SourcePath = sourcePath,
            BundlePath = Path.GetRelativePath(artifactsDir, destinationPath),
            Exists = true,
            Bytes = new FileInfo(sourcePath).Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath).ToString("O", CultureInfo.InvariantCulture),
        });

        return true;
    }

    private static string BuildBundleRelativePath(string repoRoot, string sourcePath)
    {
        string fullRepoRoot = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullSourcePath = Path.GetFullPath(sourcePath);

        if (fullSourcePath.StartsWith(fullRepoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullSourcePath, fullRepoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(fullRepoRoot, fullSourcePath);
        }

        string root = Path.GetPathRoot(fullSourcePath) ?? string.Empty;
        string rootLabel = string.IsNullOrWhiteSpace(root)
            ? "root"
            : root.Length >= 2 && root[1] == ':'
                ? $"{char.ToUpperInvariant(root[0])}_"
                : root.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '_')
                    .Replace(Path.AltDirectorySeparatorChar, '_');

        string remainder = fullSourcePath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(remainder))
        {
            remainder = Path.GetFileName(fullSourcePath);
        }

        return Path.Combine("external", rootLabel, remainder);
    }

    private static List<WindowRecord> BuildWindowInventory()
    {
        var windows = RiftWindowService.FindRiftWindows();
        var inventory = new List<WindowRecord>(windows.Count);

        foreach (var window in windows)
        {
            var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
            inventory.Add(new WindowRecord
            {
                ProcessId = window.ProcessId,
                Hwnd = RiftWindowService.FormatHwnd(window.Hwnd),
                Title = window.Title,
                ProcessName = window.ProcessName,
                ClientWidth = snapshot.ClientWidth,
                ClientHeight = snapshot.ClientHeight,
                WindowWidth = snapshot.WindowWidth,
                WindowHeight = snapshot.WindowHeight,
                WindowLeft = snapshot.WindowLeft,
                WindowTop = snapshot.WindowTop,
                ClientLeft = snapshot.ClientLeft,
                ClientTop = snapshot.ClientTop,
                IsMinimized = snapshot.IsMinimized,
            });
        }

        return inventory;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "LeaderDecoder"))
                && Directory.Exists(Path.Combine(directory.FullName, "LeaderTraceBundle")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static string? FindSavedInterfaceRoot(string repoRoot)
    {
        string repoDirectory = Path.GetFullPath(repoRoot);
        var directory = new DirectoryInfo(repoDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Saved");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        foreach (string baseDirectory in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        })
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                continue;
            }

            string candidate = Path.Combine(baseDirectory, "RIFT", "Interface", "Saved");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string? oneDriveRoot in new[]
        {
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveCommercial"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
        })
        {
            if (string.IsNullOrWhiteSpace(oneDriveRoot))
            {
                continue;
            }

            string candidate = Path.Combine(oneDriveRoot, "Documents", "RIFT", "Interface", "Saved");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));

    private static string SanitizeName(string value)
    {
        string safe = value.Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            return "trace";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return safe;
    }

    private static bool TryParseOptions(string[] args, out Options options, out string? error)
    {
        options = new Options();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "/?":
                    options.Help = true;
                    break;
                case "--output-dir":
                    if (!TryReadString(args, ref i, out string? outputDir, out error)) return false;
                    options.OutputDir = outputDir;
                    break;
                case "--name":
                    if (!TryReadString(args, ref i, out string? name, out error)) return false;
                    options.Name = name;
                    break;
                case "--max-images":
                    if (!TryReadInt(args, ref i, out int maxImages, out error)) return false;
                    options.MaxImages = Math.Max(0, maxImages);
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadString(string[] args, ref int index, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Length)
        {
            error = $"Missing value for {args[index]}.";
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt(string[] args, ref int index, out int value, out string? error)
    {
        value = 0;
        error = null;
        if (!TryReadString(args, ref index, out string? text, out error))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid integer value '{text}'.";
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        WriteHeader("USAGE");
        WriteUsage("LeaderTraceBundle.exe");
        WriteUsage("LeaderTraceBundle.exe --name nightly --output-dir trace-bundles --max-images 20");
        Console.WriteLine();
        WriteHeader("OPTIONS");
        WriteOption("--output-dir PATH", "Directory where the timestamped bundle folder is created");
        WriteOption("--name NAME", "Bundle name suffix used in the timestamped folder name");
        WriteOption("--max-images N", "Max recent images to copy from debug folders (default: 20)");
        WriteOption("--help", "Show this help");
    }

    private static void WriteHeader(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    private static void WriteUsage(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  ");
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    private static void WriteOption(string flag, string description)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"  {flag.PadRight(22)}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(description);
        Console.ForegroundColor = prev;
    }

    private sealed class Options
    {
        public bool Help { get; set; }
        public string? OutputDir { get; set; }
        public string? Name { get; set; }
        public int MaxImages { get; set; } = 20;
    }

    private sealed class ArtifactRecord
    {
        public required string Kind { get; init; }
        public required string SourcePath { get; init; }
        public required string BundlePath { get; init; }
        public required bool Exists { get; init; }
        public long Bytes { get; init; }
        public string? LastWriteTimeUtc { get; init; }
        public string? Reason { get; init; }
    }

    private sealed class WindowRecord
    {
        public required int ProcessId { get; init; }
        public required string Hwnd { get; init; }
        public required string Title { get; init; }
        public required string ProcessName { get; init; }
        public int? ClientWidth { get; init; }
        public int? ClientHeight { get; init; }
        public int? WindowWidth { get; init; }
        public int? WindowHeight { get; init; }
        public int? WindowLeft { get; init; }
        public int? WindowTop { get; init; }
        public int? ClientLeft { get; init; }
        public int? ClientTop { get; init; }
        public bool IsMinimized { get; init; }
    }
}
