using System.Buffers.Binary;

namespace InvisibleAI.Helper.AI;

public static class ImageInput
{
    public static void Validate(byte[] image)
    {
        if (image.Length < 33 || image.Length > 5 * 1024 * 1024 || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !image.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new AIProviderException("Could not read the selected region.");
        int width = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16, 4)), height = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(20, 4));
        if (width is < 1 or > 2048 || height is < 1 or > 2048) throw new AIProviderException("Selected region exceeds the image size limit.");
    }
}
