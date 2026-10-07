using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InvisibleAI.Shared.Protocol;

public static class Framing
{
    // Deliberately smaller than Chromium's inbound cap. Base64 image requests fit in 8 MiB.
    public const int MaxRequestBytes = 8 * 1024 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    public static async Task<Message?> ReadAsync(Stream stream, CancellationToken ct, int maxBytes = MaxRequestBytes)
    {
        var header = new byte[4];
        var count = await stream.ReadAsync(header.AsMemory(0, 4), ct);
        if (count == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(count), ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes) throw new ProtocolException("Message size is outside the allowed range.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        var message = JsonSerializer.Deserialize<Message>(body, Message.Json) ?? throw new ProtocolException("Empty message.");
        message.Validate();
        return message;
    }
    public static async Task WriteAsync(Stream stream, Message message, CancellationToken ct, int maxBytes = MaxResponseBytes)
    {
        message.Validate();
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(message, Message.Json);
        if (data.Length > maxBytes) throw new ProtocolException("Message exceeds the allowed size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }
}
