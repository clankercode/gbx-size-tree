using GbxSizeTree.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Re-encodes the JPEG stored by header chunk 0x03043007; see docs/FORMAT-NOTES.md §CGameCtnChallenge chunk map.
/// </summary>
public sealed class ImageSharpJpegRecoder : IJpegRecoder
{
    public byte[]? Recode(byte[] jpeg, int? maxDimension, int quality)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        try
        {
            using var image = Image.Load(jpeg);
            if (maxDimension is int limit && Math.Max(image.Width, image.Height) > limit)
            {
                image.Mutate(operation => operation.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(limit, limit),
                }));
            }

            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = quality });
            var result = output.ToArray();
            return result.Length < jpeg.Length ? result : null;
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }
}
