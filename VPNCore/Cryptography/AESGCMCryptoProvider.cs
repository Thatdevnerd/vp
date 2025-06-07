using System.Security.Cryptography;

namespace VPNCore.Cryptography;

public class AESGCMCryptoProvider : ICryptoProvider
{
    private readonly RandomNumberGenerator _rng;

    public AESGCMCryptoProvider()
    {
        _rng = RandomNumberGenerator.Create();
    }

    public byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
    {
        try
        {
            using var aes = new AesGcm(key, 16); // 16-byte tag
            var ciphertext = new byte[data.Length];
            var tag = new byte[16]; // 128-bit tag
            
            aes.Encrypt(iv, data, ciphertext, tag);
            
            // Combine ciphertext and tag
            var result = new byte[ciphertext.Length + tag.Length];
            Array.Copy(ciphertext, 0, result, 0, ciphertext.Length);
            Array.Copy(tag, 0, result, ciphertext.Length, tag.Length);
            
            return result;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Encryption failed", ex);
        }
    }

    public byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv)
    {
        try
        {
            if (encryptedData.Length < 16)
                throw new CryptographicException("Invalid encrypted data length");
                
            using var aes = new AesGcm(key, 16); // 16-byte tag
            
            // Split ciphertext and tag
            var ciphertext = new byte[encryptedData.Length - 16];
            var tag = new byte[16];
            Array.Copy(encryptedData, 0, ciphertext, 0, ciphertext.Length);
            Array.Copy(encryptedData, ciphertext.Length, tag, 0, tag.Length);
            
            var plaintext = new byte[ciphertext.Length];
            aes.Decrypt(iv, ciphertext, tag, plaintext);
            
            return plaintext;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Decryption failed", ex);
        }
    }

    public byte[] GenerateKey(int keySize)
    {
        var key = new byte[keySize / 8];
        _rng.GetBytes(key);
        return key;
    }

    public byte[] GenerateIV(int ivSize)
    {
        // AES-GCM requires 12-byte (96-bit) nonce
        var iv = new byte[12];
        _rng.GetBytes(iv);
        return iv;
    }

    public byte[] ComputeHash(byte[] data, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    public bool VerifyHash(byte[] data, byte[] hash, byte[] key)
    {
        var computedHash = ComputeHash(data, key);
        return computedHash.SequenceEqual(hash);
    }

    public (byte[] publicKey, byte[] privateKey) GenerateKeyPair(int keySize)
    {
        // Simplified implementation using .NET RSA
        using var rsa = RSA.Create(keySize);
        var publicKey = rsa.ExportRSAPublicKey();
        var privateKey = rsa.ExportRSAPrivateKey();
        return (publicKey, privateKey);
    }

    public byte[] Sign(byte[] data, byte[] privateKey)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);
            return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Signing failed", ex);
        }
    }

    public bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(publicKey, out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public byte[] DeriveSharedSecret(byte[] privateKey, byte[] publicKey)
    {
        // For RSA, we'll use a simple key derivation
        // In production, consider using ECDH for better performance
        using var sha256 = SHA256.Create();
        var combined = new byte[privateKey.Length + publicKey.Length];
        Array.Copy(privateKey, 0, combined, 0, privateKey.Length);
        Array.Copy(publicKey, 0, combined, privateKey.Length, publicKey.Length);
        return sha256.ComputeHash(combined);
    }
}