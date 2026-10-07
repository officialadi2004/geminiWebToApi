using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvisibleAI.Shared.Protocol;

public sealed record Message(string Type, string Id, JsonElement? Payload = null, int Version = 1)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    public static Message Create(string type, string id, object? payload = null) =>
        new(type, id, payload is null ? null : JsonSerializer.SerializeToElement(payload, Json));
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Id) || Id.Length > 128 ||
            string.IsNullOrWhiteSpace(Type) || Type.Length > 64)
            throw new ProtocolException("Invalid message envelope or unsupported protocol version.");
    }
}

public sealed class ProtocolException(string message) : Exception(message);
