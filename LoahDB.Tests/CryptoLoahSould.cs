
using LoahDB; 

namespace LoahDB.Tests
{
    
    public class CryptoLoahShould
    {
        private string _key;
        public CryptoLoahShould()
        {
            _key="Hello world!!!!!!!!!!!";
        }
        [Fact]
        public void EncryptAndDecryptTextSuccessfully()
        {
            // Arrange
            string plainText = "Hello, world!";

            // Act
            string encryptedText = CryptoLoah.Encrypt(plainText,_key);
            string decryptedText = CryptoLoah.Decrypt(encryptedText,_key);

            // Assert
            Assert.Equal(plainText, decryptedText);
        }

        [Fact]
        public void EncryptAndDecryptEmptyTextSuccessfully()
        {
            // Arrange
            string plainText = "";
           

            // Act
            string encryptedText = CryptoLoah.Encrypt(plainText,_key);
            string decryptedText = CryptoLoah.Decrypt(encryptedText,_key);

            // Assert
            Assert.Equal(plainText, decryptedText);
        }

        [Fact]
        public void EncryptAndDecryptLongTextSuccessfully()
        {
            // Arrange
            string plainText = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

            // Act
            string encryptedText = CryptoLoah.Encrypt(plainText,_key);
            string decryptedText = CryptoLoah.Decrypt(encryptedText,_key);

            // Assert
            Assert.Equal(plainText, decryptedText);
        }
    }
}
