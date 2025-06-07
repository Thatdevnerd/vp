using System.IO.Compression;

namespace VPNCore.Cryptography;

public class GZipCompressionProvider : ICompressionProvider
{
    private const double CompressionThreshold = 0.9; // Only compress if we save at least 10%

    public byte[] Compress(byte[] data)
    {
        if (data == null || data.Length == 0)
            return data ?? Array.Empty<byte>();

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    public byte[] Decompress(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
            return compressedData ?? Array.Empty<byte>();

        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        
        gzip.CopyTo(output);
        return output.ToArray();
    }

    public bool IsCompressionBeneficial(byte[] data)
    {
        if (data == null || data.Length < 100) // Don't compress small data
            return false;

        var compressed = Compress(data);
        return compressed.Length < (data.Length * CompressionThreshold);
    }
}