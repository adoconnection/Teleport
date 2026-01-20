using System;
using System.Security.Cryptography;
using System.Text;

namespace Teleport.Shared
{
    public static class Crypto
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        public static byte[] DeriveKey(string accessKey)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(accessKey));
        }

        public static byte[] Encrypt(byte[] plaintext, byte[] key)
        {
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

            return result;
        }

        public static byte[] Decrypt(byte[] encrypted, byte[] key)
        {
            if (encrypted.Length < NonceSize + TagSize)
                throw new CryptographicException("Invalid encrypted data");

            var nonce = new byte[NonceSize];
            var ciphertextLength = encrypted.Length - NonceSize - TagSize;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(encrypted, NonceSize, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(encrypted, NonceSize + ciphertextLength, tag, 0, TagSize);

            var plaintext = new byte[ciphertextLength];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
    }
}
