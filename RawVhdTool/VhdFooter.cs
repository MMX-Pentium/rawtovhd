using System.Buffers.Binary;
using System.Text;

namespace RawVhdTool;

internal sealed class VhdFooter
{
    public const int SectorSize = 512;
    private const uint FixedDiskType = 2;
    private static readonly byte[] Cookie = "conectix"u8.ToArray();

    public long CurrentSize { get; private init; }

    public static bool HasCookie(ReadOnlySpan<byte> bytes) =>
        bytes.Length == SectorSize && bytes[..8].SequenceEqual(Cookie);

    public static byte[] Create(long size)
    {
        if (size <= 0 || size % SectorSize != 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        byte[] bytes = new byte[SectorSize];
        Cookie.CopyTo(bytes, 0);
        WriteUInt32(bytes, 8, 0x00000002);             // Features: reserved bit required by spec
        WriteUInt32(bytes, 12, 0x00010000);            // File format version 1.0
        WriteUInt64(bytes, 16, ulong.MaxValue);         // No dynamic disk header
        DateTimeOffset epoch = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        uint timestamp = checked((uint)(DateTimeOffset.UtcNow - epoch).TotalSeconds);
        WriteUInt32(bytes, 24, timestamp);
        Encoding.ASCII.GetBytes("RVHD").CopyTo(bytes, 28);
        WriteUInt32(bytes, 32, 0x00010000);             // Creator version 1.0
        Encoding.ASCII.GetBytes("Wi2k").CopyTo(bytes, 36);
        WriteUInt64(bytes, 40, checked((ulong)size));
        WriteUInt64(bytes, 48, checked((ulong)size));

        DiskGeometry geometry = DiskGeometry.FromSize(size);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(56, 2), geometry.Cylinders);
        bytes[58] = geometry.Heads;
        bytes[59] = geometry.SectorsPerTrack;
        WriteUInt32(bytes, 60, FixedDiskType);

        Guid.NewGuid().TryWriteBytes(bytes.AsSpan(68, 16), bigEndian: true, out _);
        bytes[84] = 0;                                  // Saved state = false

        WriteUInt32(bytes, 64, CalculateChecksum(bytes));
        return bytes;
    }

    public static VhdFooter ParseAndValidate(ReadOnlySpan<byte> bytes, long fileLength)
    {
        if (bytes.Length != SectorSize)
            throw new CommandLineException("VHDフッターのサイズが不正です。");
        if (!HasCookie(bytes))
            throw new CommandLineException("VHDフッターのcookieが見つかりません。");
        if (ReadUInt32(bytes, 12) != 0x00010000)
            throw new CommandLineException("未対応のVHDファイル形式です。");
        if (ReadUInt32(bytes, 60) != FixedDiskType)
            throw new CommandLineException("固定VHDではありません。動的VHDや差分VHDには対応していません。");

        uint storedChecksum = ReadUInt32(bytes, 64);
        byte[] copy = bytes.ToArray();
        copy.AsSpan(64, 4).Clear();
        if (storedChecksum != CalculateChecksum(copy))
            throw new CommandLineException("VHDフッターのチェックサムが一致しません。");

        ulong originalSize = ReadUInt64(bytes, 40);
        ulong currentSize = ReadUInt64(bytes, 48);
        if (originalSize != currentSize)
            throw new CommandLineException("固定VHDのoriginal sizeとcurrent sizeが一致しません。");
        if (currentSize == 0 || currentSize > long.MaxValue || currentSize % SectorSize != 0)
            throw new CommandLineException("VHDに記録された仮想ディスク容量が不正です。");
        if ((ulong)(fileLength - SectorSize) != currentSize)
            throw new CommandLineException("VHDのデータ領域サイズとフッターに記録された容量が一致しません。");

        return new VhdFooter { CurrentSize = (long)currentSize };
    }

    private static uint CalculateChecksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        foreach (byte value in bytes)
            sum += value;
        return ~sum;
    }

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(offset, 4), value);

    private static void WriteUInt64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(offset, 8), value);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(offset, 8));

    private readonly record struct DiskGeometry(ushort Cylinders, byte Heads, byte SectorsPerTrack)
    {
        public static DiskGeometry FromSize(long size)
        {
            ulong totalSectors = (ulong)size / SectorSize;
            const ulong maxSectors = 65535UL * 16 * 255;
            totalSectors = Math.Min(totalSectors, maxSectors);

            ulong sectorsPerTrack;
            ulong heads;
            ulong cylinderTimesHeads;

            if (totalSectors >= 65535UL * 16 * 63)
            {
                sectorsPerTrack = 255;
                heads = 16;
                cylinderTimesHeads = totalSectors / sectorsPerTrack;
            }
            else
            {
                sectorsPerTrack = 17;
                cylinderTimesHeads = (totalSectors + sectorsPerTrack - 1) / sectorsPerTrack;
                heads = (cylinderTimesHeads + 1023) / 1024;
                heads = Math.Max(heads, 4);

                if (cylinderTimesHeads >= heads * 1024 || heads > 16)
                {
                    sectorsPerTrack = 31;
                    heads = 16;
                    cylinderTimesHeads = (totalSectors + sectorsPerTrack - 1) / sectorsPerTrack;
                }
                if (cylinderTimesHeads >= heads * 1024)
                {
                    sectorsPerTrack = 63;
                    heads = 16;
                    cylinderTimesHeads = (totalSectors + sectorsPerTrack - 1) / sectorsPerTrack;
                }
            }

            ulong cylinders = cylinderTimesHeads / heads;
            return new DiskGeometry((ushort)Math.Min(cylinders, ushort.MaxValue), (byte)heads, (byte)sectorsPerTrack);
        }
    }
}
