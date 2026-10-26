using Ponte.Core;

static class FileSessionTests
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "session-library");
        var original = Path.Combine(root, "original.txt");
        var exported = Path.Combine(root, "saved-copy.txt");
        await File.WriteAllTextAsync(original, "keep original");
        var library = new FileLibrary(path);
        await using (var input = File.OpenRead(original)) await library.AddAsync(input, "original.txt", "Windows");
        await File.WriteAllTextAsync(exported, "keep exported");
        await File.WriteAllTextAsync(Path.Combine(path, "unrelated.txt"), "not a Ponte artifact");
        await File.WriteAllTextAsync(Path.Combine(path, "unrelated.json"), "not library metadata");
        await File.WriteAllTextAsync(Path.Combine(path, Guid.NewGuid().ToString("N") + ".partial"), "interrupted upload");
        var nested = Path.Combine(path, "untouched-subfolder");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "original.bin"), "keep nested");
        check(new FileLibrary(path).List().Count == 1, "Reconnect/read-only library reload does not clear current session");
        FileLibrary.BeginNewSession(path);
        check(library.List().Count == 0 && !Directory.EnumerateFiles(path, "*.bin").Any(), "New PC session clears metadata and internal file copies");
        check(!Directory.EnumerateFiles(path, "*.partial").Any(), "New PC session also removes interrupted uploads");
        check(await File.ReadAllTextAsync(original) == "keep original" && await File.ReadAllTextAsync(exported) == "keep exported", "Session cleanup preserves originals and explicitly saved copies");
        check(File.Exists(Path.Combine(path, "unrelated.txt")) && File.Exists(Path.Combine(path, "unrelated.json")) && File.Exists(Path.Combine(nested, "original.bin")), "Cleanup is non-recursive and preserves unrecognized files");
        check(FileLibrary.BeginNewSession(path) == 0, "Repeated empty session cleanup is harmless");
        // A stream can report > 1 GiB without being rejected before streaming.
        await library.AddAsync(new LargeDeclaredStream(), "large-provider.bin", "test");
        check(library.List().Single().Size == 3, "Default library has no declared-size 1 GiB rejection; streams actual bytes");
    }
    private sealed class LargeDeclaredStream : MemoryStream
    {
        public LargeDeclaredStream() : base(new byte[] { 1, 2, 3 }) { }
        public override long Length => 3L * 1024 * 1024 * 1024;
    }
}
