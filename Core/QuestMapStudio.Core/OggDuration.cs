using System;
using System.Buffers.Binary;

namespace QuestMapStudio.Core;

public static class OggDuration
{
	public static double? Read(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
	{
		if (!PageAt(head, 0, out var payload, out var size) || payload >= head.Length)
		{
			return null;
		}
		ReadOnlySpan<byte> readOnlySpan = head.Slice(payload);
		int num;
		int num2;
		if (readOnlySpan.Length >= 16 && readOnlySpan.Slice(0, 7).SequenceEqual("\u0001vorbis"u8))
		{
			num = BinaryPrimitives.ReadInt32LittleEndian(readOnlySpan.Slice(12, 4));
			num2 = 0;
		}
		else
		{
			if (readOnlySpan.Length < 12 || !readOnlySpan.Slice(0, 8).SequenceEqual("OpusHead"u8))
			{
				return null;
			}
			num = 48000;
			num2 = BinaryPrimitives.ReadUInt16LittleEndian(readOnlySpan.Slice(10, 2));
		}
		if (num <= 0)
		{
			return null;
		}
		ReadOnlySpan<byte> other = head.Slice(14, 4);
		long num3 = -1L;
		for (int i = 0; i + 27 <= tail.Length; i++)
		{
			if (PageAt(tail, i, out size, out var size2))
			{
				if ((tail[i + 5] & 4) != 0 && tail.Slice(i + 14, 4).SequenceEqual(other))
				{
					num3 = BinaryPrimitives.ReadInt64LittleEndian(tail.Slice(i + 6, 8));
				}
				i += size2 - 1;
			}
		}
		if (num3 <= num2)
		{
			return null;
		}
		return (double)(num3 - num2) / (double)num;
	}

	private static bool PageAt(ReadOnlySpan<byte> data, int offset, out int payload, out int size)
	{
		payload = (size = 0);
		if (data.Length - offset < 27 || !data.Slice(offset, 5).SequenceEqual("OggS\0"u8))
		{
			return false;
		}
		int num = data[offset + 26];
		if (data.Length - offset < 27 + num)
		{
			return false;
		}
		payload = offset + 27 + num;
		int num2 = 0;
		ReadOnlySpan<byte> readOnlySpan = data.Slice(offset + 27, num);
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			byte b = readOnlySpan[i];
			num2 += b;
		}
		size = 27 + num + num2;
		return data.Length - offset >= size;
	}
}
