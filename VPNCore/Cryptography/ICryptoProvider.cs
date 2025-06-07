namespace VPNCore.Cryptography;

public interface ICryptoProvider
{
    byte[] Encrypt(byte[] data, byte[] key, byte[] iv);
    byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv);
    byte[] GenerateKey(int keySize);
    byte[] GenerateIV(int ivSize);
    byte[] ComputeHash(byte[] data, byte[] key);
    bool VerifyHash(byte[] data, byte[] hash, byte[] key);
    (byte[] publicKey, byte[] privateKey) GenerateKeyPair(int keySize);
    byte[] Sign(byte[] data, byte[] privateKey);
    bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey);
    byte[] DeriveSharedSecret(byte[] privateKey, byte[] publicKey);
}

public interface ICompressionProvider
{
    byte[] Compress(byte[] data);
    byte[] Decompress(byte[] compressedData);
    bool IsCompressionBeneficial(byte[] data);
}