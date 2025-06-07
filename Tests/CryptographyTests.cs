using VPNCore.Cryptography;
using Xunit;

namespace VPNTests;

public class CryptographyTests
{
    private readonly ICryptoProvider _cryptoProvider;
    private readonly ICompressionProvider _compressionProvider;

    public CryptographyTests()
    {
        _cryptoProvider = new AESGCMCryptoProvider();
        _compressionProvider = new GZipCompressionProvider();
    }

    [Fact]
    public void EncryptDecrypt_ShouldReturnOriginalData()
    {
        // Arrange
        var originalData = System.Text.Encoding.UTF8.GetBytes("Hello, VPN World!");
        var key = _cryptoProvider.GenerateKey(256);
        var iv = _cryptoProvider.GenerateIV(128);

        // Act
        var encryptedData = _cryptoProvider.Encrypt(originalData, key, iv);
        var decryptedData = _cryptoProvider.Decrypt(encryptedData, key, iv);

        // Assert
        Assert.Equal(originalData, decryptedData);
        Assert.NotEqual(originalData, encryptedData);
    }

    [Fact]
    public void GenerateKey_ShouldReturnCorrectSize()
    {
        // Act
        var key256 = _cryptoProvider.GenerateKey(256);
        var key128 = _cryptoProvider.GenerateKey(128);

        // Assert
        Assert.Equal(32, key256.Length); // 256 bits = 32 bytes
        Assert.Equal(16, key128.Length); // 128 bits = 16 bytes
    }

    [Fact]
    public void ComputeVerifyHash_ShouldWork()
    {
        // Arrange
        var data = System.Text.Encoding.UTF8.GetBytes("Test data for hashing");
        var key = _cryptoProvider.GenerateKey(256);

        // Act
        var hash = _cryptoProvider.ComputeHash(data, key);
        var isValid = _cryptoProvider.VerifyHash(data, hash, key);

        // Assert
        Assert.True(isValid);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void GenerateKeyPair_ShouldReturnValidKeys()
    {
        // Act
        var (publicKey, privateKey) = _cryptoProvider.GenerateKeyPair(2048);

        // Assert
        Assert.NotEmpty(publicKey);
        Assert.NotEmpty(privateKey);
        Assert.NotEqual(publicKey, privateKey);
    }

    [Fact]
    public void CompressDecompress_ShouldReturnOriginalData()
    {
        // Arrange
        var originalData = System.Text.Encoding.UTF8.GetBytes(new string('A', 1000)); // Repeating data compresses well

        // Act
        var compressedData = _compressionProvider.Compress(originalData);
        var decompressedData = _compressionProvider.Decompress(compressedData);

        // Assert
        Assert.Equal(originalData, decompressedData);
        Assert.True(compressedData.Length < originalData.Length); // Should be compressed
    }

    [Fact]
    public void IsCompressionBeneficial_ShouldReturnCorrectResult()
    {
        // Arrange
        var repeatData = System.Text.Encoding.UTF8.GetBytes(new string('A', 1000));
        var randomData = _cryptoProvider.GenerateKey(1000 * 8); // Random data doesn't compress well
        var smallData = System.Text.Encoding.UTF8.GetBytes("Hi");

        // Act & Assert
        Assert.True(_compressionProvider.IsCompressionBeneficial(repeatData));
        Assert.False(_compressionProvider.IsCompressionBeneficial(randomData));
        Assert.False(_compressionProvider.IsCompressionBeneficial(smallData));
    }
}