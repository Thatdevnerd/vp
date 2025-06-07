using Xunit;
using VPNCore.Cryptography;

namespace VPNTests;

public class VPNCryptographyTests
{
    private readonly VPNCryptography _crypto;

    public VPNCryptographyTests()
    {
        _crypto = new VPNCryptography();
    }

    [Fact]
    public void GenerateKeyPair_ShouldReturnValidKeys()
    {
        // Act
        var (publicKey, privateKey) = _crypto.GenerateKeyPair();

        // Assert
        Assert.NotNull(publicKey);
        Assert.NotNull(privateKey);
        Assert.True(publicKey.Length > 0);
        Assert.True(privateKey.Length > 0);
    }

    [Fact]
    public void EncryptDecrypt_ShouldReturnOriginalData()
    {
        // Arrange
        var originalData = System.Text.Encoding.UTF8.GetBytes("Hello, VPN World!");
        var key = _crypto.GenerateRandomBytes(32); // 256-bit key
        var iv = _crypto.GenerateRandomBytes(16);  // 128-bit IV for CBC

        // Act
        var encryptedData = _crypto.Encrypt(originalData, key, iv);
        var decryptedData = _crypto.Decrypt(encryptedData, key, iv);

        // Assert
        Assert.NotEqual(originalData, encryptedData);
        Assert.Equal(originalData, decryptedData);
    }

    [Fact]
    public void GenerateRandomBytes_ShouldReturnCorrectLength()
    {
        // Arrange
        var length = 32;

        // Act
        var randomBytes = _crypto.GenerateRandomBytes(length);

        // Assert
        Assert.Equal(length, randomBytes.Length);
    }

    [Fact]
    public void SignAndVerify_ShouldWork()
    {
        // Arrange
        var data = System.Text.Encoding.UTF8.GetBytes("Test data for signing");
        var (publicKey, privateKey) = _crypto.GenerateKeyPair();

        // Act
        var signature = _crypto.SignData(data, privateKey);
        var isValid = _crypto.VerifySignature(data, signature, publicKey);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifySignature_WithWrongData_ShouldReturnFalse()
    {
        // Arrange
        var originalData = System.Text.Encoding.UTF8.GetBytes("Original data");
        var modifiedData = System.Text.Encoding.UTF8.GetBytes("Modified data");
        var (publicKey, privateKey) = _crypto.GenerateKeyPair();

        // Act
        var signature = _crypto.SignData(originalData, privateKey);
        var isValid = _crypto.VerifySignature(modifiedData, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }
}