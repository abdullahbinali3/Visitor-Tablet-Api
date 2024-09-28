using System.Security.Cryptography;

namespace VisitorTabletAPITemplate.Utilities
{
    /* Inspired by kitsu.eb's StackOverflow answer, but rewritten by Shane.
     * https://stackoverflow.com/questions/54991/generating-random-passwords
     */
    public static class PasswordGenerator
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string number = "1234567890";
        const string special = "!@#$%^&*()[]{},.:`~_-=+"; // excludes problematic characters like ;'"/\
        const string lowerUpperNumber = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        const string lowerUpperNumberSpecial = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()[]{},.:`~_-=+";

        const int lowerLength = 26; // lower.Length
        const int upperLength = 26; // upper.Length;
        const int numberLength = 10; // number.Length;
        const int specialLength = 23; // special.Length;

        /// <summary>
        /// Generates a random password consisting of lowercase and uppercase letters, numbers and symbols.
        /// </summary>
        /// <param name="length">The length of the generated password.</param>
        /// <param name="characterTypesHaveEqualProbability">Whether each type of character (lowercase, uppercase, number, symbol) is selected with equal probability.<br/>
        /// Since the number of possible letters is greater than the number of possible numbers, when true, this lowers the entropy of the password, but may work better for shorter passwords, as the possiblity of the password having no numbers at all is lower.</param>
        /// <returns></returns>
        public static string Generate(int length = 96, bool characterTypesHaveEqualProbability = false)
        {
            // If all individual characters have equal probability (instead of each character type), use RandomNumberGenerator.GetItems() method added in .NET 8
            if (!characterTypesHaveEqualProbability)
            {
                return new string(RandomNumberGenerator.GetItems<char>(lowerUpperNumberSpecial, length));
            }

            Span<char> result = length < 1024 ? stackalloc char[length] : new char[length].AsSpan();

            for (int i = 0; i < length; ++i)
            {
                switch (RandomNumberGenerator.GetInt32(4))
                {
                    case 0:
                        result[i] = lower[RandomNumberGenerator.GetInt32(0, lowerLength)];
                        break;
                    case 1:
                        result[i] = upper[RandomNumberGenerator.GetInt32(0, upperLength)];
                        break;
                    case 2:
                        result[i] = number[RandomNumberGenerator.GetInt32(0, numberLength)];
                        break;
                    case 3:
                        result[i] = special[RandomNumberGenerator.GetInt32(0, specialLength)];
                        break;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Generates a random password consisting of lowercase and uppercase letters and numbers, without symbols.
        /// </summary>
        /// <param name="length">The length of the generated password.</param>
        /// <param name="characterTypesHaveEqualProbability">Whether each type of character (lowercase, uppercase, number) is selected with equal probability.<br/>
        /// Since the number of possible letters is greater than the number of possible numbers, when true, this lowers the entropy of the password, but may work better for shorter passwords, as the possiblity of the password having no numbers at all is lower.</param>
        /// <returns></returns>
        public static string GenerateAlphanumeric(int length = 96, bool characterTypesHaveEqualProbability = false)
        {
            // If all individual characters have equal probability (instead of each character type), use RandomNumberGenerator.GetItems() method added in .NET 8
            if (!characterTypesHaveEqualProbability)
            {
                return new string(RandomNumberGenerator.GetItems<char>(lowerUpperNumber, length));
            }

            Span<char> result = length < 1024 ? stackalloc char[length] : new char[length].AsSpan();

            for (int i = 0; i < length; ++i)
            {
                switch (RandomNumberGenerator.GetInt32(3))
                {
                    case 0:
                        result[i] = lower[RandomNumberGenerator.GetInt32(0, lowerLength)];
                        break;
                    case 1:
                        result[i] = upper[RandomNumberGenerator.GetInt32(0, upperLength)];
                        break;
                    case 2:
                        result[i] = number[RandomNumberGenerator.GetInt32(0, numberLength)];
                        break;
                }
            }

            return result.ToString();
        }
    }
}
