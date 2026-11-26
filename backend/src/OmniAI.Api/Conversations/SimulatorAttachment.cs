using SkiaSharp;

namespace OmniAI.Api.Conversations;

internal sealed record SimulatorAttachmentPayload(string? Url);

/// <summary>Media shared by the local Messenger and Instagram event fixtures.</summary>
internal sealed record SimulatorAttachment(string? Type, SimulatorAttachmentPayload? Payload)
{
    internal const int MaxPerMessage = 10;
    internal const int MaxRequestBytes = 2_000_000;

    internal static string[]? Read(SimulatorAttachment?[]? attachments) =>
        attachments is { Length: > 0 } ? attachments.Select(item =>
            item is { Type: "image" } ? Preview(item.Payload?.Url) : "Unsupported").ToArray() : null;

    internal static string Preview(string? url)
    {
        if (url == "https://media.example.test/nabra/parcel.png")
        {
            return "SampleImage";
        }
        if (url is null || url.Length > 700_000)
        {
            return "UnavailableImage";
        }
        int prefix = url.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ? 22 :
            url.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ? 23 : 0;
        if (prefix == 0)
        {
            return "UnavailableImage";
        }

        byte[] bytes = new byte[(url.Length - prefix) * 3 / 4];
        if (!Convert.TryFromBase64Chars(url.AsSpan(prefix), bytes, out int size) || size is 0 or > 524288)
        {
            return "UnavailableImage";
        }
        using var stream = new MemoryStream(bytes, 0, size, writable: false);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg) ||
            codec.Info.Width is <= 0 or > 2048 || codec.Info.Height is <= 0 or > 2048)
        {
            return "UnavailableImage";
        }
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
        {
            return "UnavailableImage";
        }
        float scale = Math.Min(1f, 320f / Math.Max(bitmap.Width, bitmap.Height));
        using var thumbnail = new SKBitmap(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
        using var canvas = new SKCanvas(thumbnail);
        canvas.Clear(SKColors.White);
        using var source = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(source, new SKRect(0, 0, thumbnail.Width, thumbnail.Height), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(thumbnail);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 72);
        return encoded is { Size: <= 49152 } ? "data:image/jpeg;base64," + Convert.ToBase64String(encoded.ToArray()) : "UnavailableImage";
    }
}
