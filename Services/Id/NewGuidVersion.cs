using System.Security.Cryptography;

namespace BondRun.Services.Id;

public static class NewGuidVersion
{
    public static Guid CreateVersion7(DateTimeOffset timestamp)
    {
        ulong unixMs = (ulong)timestamp.ToUnixTimeMilliseconds();
        byte[] timeBytes = BitConverter.GetBytes(unixMs);
        
        if(BitConverter.IsLittleEndian) Array.Reverse(timeBytes);

        Span<byte> rand = stackalloc byte[10];
        RandomNumberGenerator.Fill(rand);
        
        Span<byte> guidBytes = stackalloc byte[16];
        
        timeBytes.AsSpan(2, 6).CopyTo(guidBytes);
        
        rand.CopyTo(guidBytes.Slice(6));
        
        guidBytes[6]  = (byte)((guidBytes[6] & 0x0F) | 0x70);
        guidBytes[8]  = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
    
    public static Guid CreateVersion7() =>
        CreateVersion7(DateTimeOffset.UtcNow);
}