using System;
using System.Security.Cryptography;
using VPNCore.Interfaces;

namespace VPNCore.Cryptography;

public class VPNCryptography : IVPNCryptography
{
    public VPNCryptography()
    {
    }

    public byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Encryption failed: {ex.Message}", ex);
        }
    }

    public byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Decryption failed: {ex.Message}", ex);
        }
    }

    public (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
    {
        try
        {
            using var ecdh = ECDiffieHellman.Create();
            var privateKey = ecdh.ExportECPrivateKey();
            var publicKey = ecdh.ExportSubjectPublicKeyInfo();
            return (publicKey, privateKey);
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Key pair generation failed: {ex.Message}", ex);
        }
    }

    public byte[] ComputeSharedSecret(byte[] privateKey, byte[] publicKey)
    {
        try
        {
            using var ecdh = ECDiffieHellman.Create();
            ecdh.ImportECPrivateKey(privateKey, out _);
            
            using var otherPartyKey = ECDiffieHellman.Create();
            otherPartyKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            return ecdh.DeriveKeyMaterial(otherPartyKey.PublicKey);
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Shared secret computation failed: {ex.Message}", ex);
        }
    }

    public byte[] GenerateRandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    public byte[] SignData(byte[] data, byte[] privateKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(privateKey, out _);
            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }
        catch (Exception ex)
        {
            throw new CryptographicException($"Data signing failed: {ex.Message}", ex);
        }
    }
}