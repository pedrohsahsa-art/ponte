using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ponte.Core;

public record Device(string Id, string Name, string TokenHash, DateTimeOffset PairedAt);
public record PairingTicket(string Code, DateTimeOffset ExpiresAt);

public sealed class DeviceRegistry
{
    private readonly object gate = new();
    private readonly string path;
    private List<Device> devices;
    private PairingTicket? ticket;
    private readonly TimeProvider clock;

    public DeviceRegistry(string directory, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "devices.json");
        devices = File.Exists(path) ? JsonSerializer.Deserialize<List<Device>>(File.ReadAllText(path)) ?? [] : [];
    }

    public PairingTicket CreateTicket()
    {
        lock (gate) return ticket = new PairingTicket(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), clock.GetUtcNow().AddMinutes(5));
    }

    public bool ConsumeTicket(string? code)
    {
        lock (gate)
        {
            if (ticket is null || ticket.ExpiresAt <= clock.GetUtcNow() || code is null || !Equal(ticket.Code, code)) return false;
            ticket = null;
            return true;
        }
    }

    public (Device Device, string Token) Register(string name)
    {
        lock (gate)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var device = new Device(Guid.NewGuid().ToString("N"), name, Hash(token), clock.GetUtcNow());
            var next = devices.Append(device).ToList();
            Save(next);
            devices = next;
            return (device, token);
        }
    }

    public bool Authenticate(string? token) => AuthenticateDevice(token) is not null;

    public Device? AuthenticateDevice(string? token)
    {
        if (token is null || token.Length != 64) return null;
        var hash = Hash(token);
        lock (gate) return devices.FirstOrDefault(d => Equal(d.TokenHash, hash));
    }

    public IReadOnlyList<Device> List() { lock (gate) return devices.ToArray(); }
    public void Revoke(string id)
    {
        lock (gate)
        {
            var next = devices.Where(d => d.Id != id).ToList();
            Save(next);
            devices = next;
        }
    }
    private void Save(List<Device> value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
