using System;
using System.IO;
using SkiaSharp;

namespace QuestMapStudio;

internal static class TrackArtwork
{
	internal static byte[] CreateThumbnail(string file)
	{
		using SKData sKData = SKData.Create(file);
		using SKCodec sKCodec = ((sKData == null) ? null : SKCodec.Create(sKData));
		if (sKCodec == null || sKCodec.Info.Width < 1 || sKCodec.Info.Height < 1 || (long)sKCodec.Info.Width * (long)sKCodec.Info.Height > 24000000)
		{
			throw new InvalidDataException("Could not open the map cover.");
		}
		SKImageInfo info = new SKImageInfo(sKCodec.Info.Width, sKCodec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using SKBitmap sKBitmap = new SKBitmap(info);
		if (sKCodec.GetPixels(info, sKBitmap.GetPixels()) != SKCodecResult.Success)
		{
			throw new InvalidDataException("The cover file could not be fully decoded.");
		}
		double num = Math.Min(1.0, 96.0 / (double)Math.Max(info.Width, info.Height));
		int width = Math.Max(1, (int)Math.Round((double)info.Width * num));
		int height = Math.Max(1, (int)Math.Round((double)info.Height * num));
		using SKBitmap bitmap = sKBitmap.Resize(new SKImageInfo(width, height, info.ColorType, info.AlphaType), SKFilterQuality.Medium) ?? throw new InvalidDataException("Could not resize the map cover.");
		using SKImage sKImage = SKImage.FromBitmap(bitmap);
		using SKData sKData2 = sKImage.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Could not encode the map cover.");
		return sKData2.ToArray();
	}
}
