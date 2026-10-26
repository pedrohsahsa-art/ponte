using System.Text.Json;

namespace Ponte.Core;

public record SharedFile(string Id, string Name, long Size, DateTimeOffset CreatedAt, string Source);

public sealed class FileLibrary
{
    private readonly string directory;
    private readonly long? maxBytes;
    private readonly SemaphoreSlim transfers = new(3);
    public FileLibrary(string directory, long? maxBytes = null)
    {
        if (maxBytes is < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.directory = directory;
        this.maxBytes = maxBytes;
        Directory.CreateDirectory(directory);
    }

    // Called ONLY on a new PC application session, before accepting transfers.
    // Never recurse or follow junctions; only delete this library's GUID artifacts.
    public static int BeginNewSession(string directory)
    {
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A biblioteca não pode ser um link/junção para outra pasta.");
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            var suffix = new[] { ".json.tmp", ".partial", ".json", ".bin" }.FirstOrDefault(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (suffix is null || !IsId(name[..^suffix.Length]) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            File.Delete(path);
            deleted++;
        }
        return deleted;
    }

    public IReadOnlyList<SharedFile> List() => Directory.EnumerateFiles(directory, "*.json")
        .Where(p => IsId(Path.GetFileNameWithoutExtension(p)))
        .Select(p => JsonSerializer.Deserialize<SharedFile>(File.ReadAllText(p))!)
        .Where(f => f is not null && IsId(f.Id) && File.Exists(Path.Combine(directory, f.Id + ".bin")))
        .OrderByDescending(f => f.CreatedAt).ToArray();

    public (SharedFile Info, string Path)? Find(string id)
    {
        if (!IsId(id)) return null;
        var meta = Path.Combine(directory, id + ".json");
        var file = Path.Combine(directory, id + ".bin");
        if (!File.Exists(meta) || !File.Exists(file)) return null;
        return (JsonSerializer.Deserialize<SharedFile>(File.ReadAllText(meta))!, file);
    }

    public async Task<SharedFile> AddAsync(Stream input, string name, string source, CancellationToken cancellationToken = default)
    {
        var cleanName = CleanName(name);
        await transfers.WaitAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var partial = Path.Combine(directory, id + ".partial");
        var destination = Path.Combine(directory, id + ".bin");
        try
        {
            long total = 0;
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (maxBytes.HasValue && total > maxBytes.Value) throw new InvalidDataException("O arquivo ultrapassou o limite configurado nesta biblioteca.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var info = new SharedFile(id, cleanName, total, DateTimeOffset.UtcNow, source);
            File.Move(partial, destination);
            var meta = Path.Combine(directory, id + ".json");
            await File.WriteAllTextAsync(meta + ".tmp", JsonSerializer.Serialize(info), cancellationToken);
            File.Move(meta + ".tmp", meta);
            return info;
        }
        catch
        {
            File.Delete(partial);
            File.Delete(destination);
            File.Delete(Path.Combine(directory, id + ".json.tmp"));
            throw;
        }
        finally { transfers.Release(); }
    }

    public static string CleanName(string name)
    {
        var invalid = "<>:\"/\\|?*";
        var clean = new string(name.Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(clean)) clean = "arquivo";
        if (clean.Length > 180) clean = clean[..180];
        // Never used as a storage path; GUIDs are the only filesystem keys.
        return clean;
    }
    public static bool IsId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigit);
}
