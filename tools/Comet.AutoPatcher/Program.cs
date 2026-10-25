using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Comet.AutoPatcher;

public sealed class PatcherConfig
{
    public string ServerName { get; set; } = "Mystic-Realms";
    public string PatchManifestUrl { get; set; } = "http://127.0.0.1:8080/patches/patches.json";
    public bool EnableAutoPatch { get; set; } = false;
    public string ClientDirectory { get; set; } = ".";
    public string LauncherExecutable { get; set; } = "Dragon.Launch.exe";
    public string ClientExecutable { get; set; } = "Conquer.exe";
    public string ClientArguments { get; set; } = "blacknull";
    public bool UseDirectConquerLoader { get; set; } = false;
    public string ConquerLoaderExecutable { get; set; } = "ConquerLoader.exe";
}

public sealed class PatchEntry
{
    public int Version { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class PatchManifest
{
    public int LatestVersion { get; set; }
    public List<PatchEntry> Patches { get; set; } = new();
}

public static class Program
{
    private const string ConfigFileName = "PatcherConfig.json";
    private const string VersionFileName = "comet_patch_version.txt";

    public static async Task<int> Main(string[] args)
    {
        // Admin utility mode: Comet.AutoPatcher --generate-manifest <folder> <baseUrl>
        if (args.Length >= 2 && args[0].Equals("--generate-manifest", StringComparison.OrdinalIgnoreCase))
        {
            string folder = args[1];
            string baseUrl = args.Length >= 3 ? args[2].TrimEnd('/') : "http://127.0.0.1:8080/patches";
            return GenerateManifest(folder, baseUrl);
        }

        Console.Title = "Mystic-Realms (5187) - AutoPatcher & Launcher";
        PatcherConfig config = LoadConfig();
        Console.WriteLine($"=== {config.ServerName} Launcher ===");

        string clientDir = Path.GetFullPath(config.ClientDirectory);

        if (config.EnableAutoPatch)
        {
            try
            {
                await RunAutoPatchAsync(config, clientDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoPatch] Error: {ex.Message}");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                return 1;
            }
        }
        else
        {
            Console.WriteLine("[AutoPatch] Disabled (Clean 5187 Client mode). Skipping patch check...");
        }

        return LaunchGame(config, clientDir);
    }

    private static PatcherConfig LoadConfig()
    {
        if (!File.Exists(ConfigFileName))
            return new PatcherConfig();

        string json = File.ReadAllText(ConfigFileName);
        return JsonSerializer.Deserialize<PatcherConfig>(json) ?? new PatcherConfig();
    }

    private static async Task RunAutoPatchAsync(PatcherConfig config, string clientDir)
    {
        string versionFilePath = Path.Combine(clientDir, VersionFileName);
        int currentVersion = 0;
        if (File.Exists(versionFilePath) && int.TryParse(File.ReadAllText(versionFilePath).Trim(), out int parsed))
            currentVersion = parsed;

        Console.WriteLine($"[AutoPatch] Current client patch version: {currentVersion}");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        string manifestJson = await http.GetStringAsync(config.PatchManifestUrl);
        PatchManifest manifest = JsonSerializer.Deserialize<PatchManifest>(manifestJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PatchManifest();

        var pending = manifest.Patches
            .Where(p => p.Version > currentVersion)
            .OrderBy(p => p.Version)
            .ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("[AutoPatch] Client is up to date.");
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "CometAutoPatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (PatchEntry patch in pending)
            {
                Console.WriteLine($"[AutoPatch] Downloading patch v{patch.Version} ({patch.FileName})...");
                string zipPath = Path.Combine(tempDir, patch.FileName);

                await using (Stream netStream = await http.GetStreamAsync(patch.Url))
                await using (FileStream fileStream = File.Create(zipPath))
                {
                    await netStream.CopyToAsync(fileStream);
                }

                string actualHash = ComputeSha256(zipPath);
                if (!string.IsNullOrWhiteSpace(patch.Sha256) &&
                    !actualHash.Equals(patch.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SHA-256 mismatch on {patch.FileName}. Expected {patch.Sha256}, got {actualHash}.");
                }

                Console.WriteLine($"[AutoPatch] Extracting patch v{patch.Version}...");
                ZipFile.ExtractToDirectory(zipPath, clientDir, overwriteFiles: true);
                File.WriteAllText(versionFilePath, patch.Version.ToString());
                File.Delete(zipPath);
            }

            Console.WriteLine($"[AutoPatch] Updated successfully to v{manifest.LatestVersion}.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static int LaunchGame(PatcherConfig config, string clientDir)
    {
        if (config.UseDirectConquerLoader)
        {
            string loaderPath = Path.Combine(clientDir, config.ConquerLoaderExecutable);
            if (!File.Exists(loaderPath))
            {
                Console.WriteLine($"[Launch] Could not find {loaderPath}");
                return 2;
            }

            Process.Start(new ProcessStartInfo
            {
                WorkingDirectory = clientDir,
                FileName = loaderPath,
                UseShellExecute = true
            });
            return 0;
        }

        string dragonPath = Path.Combine(clientDir, config.LauncherExecutable);
        if (!File.Exists(dragonPath))
        {
            Console.WriteLine($"[Launch] Could not find {dragonPath}");
            return 2;
        }

        Process.Start(new ProcessStartInfo
        {
            WorkingDirectory = clientDir,
            FileName = dragonPath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"\"{clientDir}\" {config.ClientExecutable} {config.ClientArguments}"
        });

        return 0;
    }

    private static int GenerateManifest(string folder, string baseUrl)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Folder not found: {folder}");
            return 1;
        }

        var manifest = new PatchManifest();
        foreach (string file in Directory.GetFiles(folder, "*.zip").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(name, out int version))
            {
                Console.WriteLine($"Skipping {Path.GetFileName(file)} (filename must be numeric like 1001.zip)");
                continue;
            }

            var info = new FileInfo(file);
            manifest.Patches.Add(new PatchEntry
            {
                Version = version,
                FileName = info.Name,
                Url = $"{baseUrl}/{info.Name}",
                Sha256 = ComputeSha256(file),
                SizeBytes = info.Length
            });
        }

        manifest.Patches = manifest.Patches.OrderBy(p => p.Version).ToList();
        manifest.LatestVersion = manifest.Patches.LastOrDefault()?.Version ?? 0;

        string outputPath = Path.Combine(folder, "patches.json");
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        Console.WriteLine($"Generated {outputPath} with {manifest.Patches.Count} patch(es). Latest: {manifest.LatestVersion}");
        return 0;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
