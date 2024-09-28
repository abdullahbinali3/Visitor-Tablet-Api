namespace VisitorTabletAPITemplate.Utilities
{
    public static class EnumParser
    {
        /// <summary>
        /// Returns true if the specified enum contains an item whose value matches the given integer, or false if not.
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumInt"></param>
        /// <returns></returns>
        public static bool IsValidEnum<TEnum>(int enumInt) where TEnum : struct, Enum
        {
            return Enum.IsDefined(typeof(TEnum), enumInt);
        }

        /// <summary>
        /// Returns true if the specified enum contains an item whose value matches the given integer, or false if not.
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumInt"></param>
        /// <returns></returns>
        public static bool IsValidEnum<TEnum>(int? enumInt) where TEnum : struct, Enum
        {
            if (!enumInt.HasValue)
            {
                return false;
            }

            return Enum.IsDefined(typeof(TEnum), enumInt.Value);
        }

        /// <summary>
        /// Returns true if the specified enum's value matches an item in the enum, or false if not.
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        public static bool IsValidEnum<TEnum>(TEnum enumValue) where TEnum : struct, Enum
        {
            return Enum.IsDefined(enumValue);
        }

        /// <summary>
        /// Returns true if the specified enum's value matches an item in the enum, or false if not.
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        public static bool IsValidEnum<TEnum>(TEnum? enumValue) where TEnum : struct, Enum
        {
            if (!enumValue.HasValue)
            {
                return false;
            }

            return Enum.IsDefined(enumValue.Value);
        }

        /// <summary>
        /// <para>Outputs an enum whose value matches the given integer.</para>
        /// <para>Returns true if the integer could be mapped to the enum, or false if not.</para>
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumInt"></param>
        /// <param name="parsedEnum"></param>
        /// <returns></returns>
        public static bool TryParseEnum<TEnum>(int enumInt, out TEnum? parsedEnum) where TEnum : struct, Enum
        {
            parsedEnum = default;

            if (IsValidEnum<TEnum>(enumInt))
            {
                parsedEnum = (TEnum)(object)enumInt;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <para>Outputs an enum whose value matches the given integer.</para>
        /// <para>Returns true if the integer could be mapped to the enum, or false if not.</para>
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="enumInt"></param>
        /// <param name="parsedEnum"></param>
        /// <returns></returns>
        public static bool TryParseEnum<TEnum>(int? enumInt, out TEnum? parsedEnum) where TEnum : struct, Enum
        {
            parsedEnum = default;

            if (IsValidEnum<TEnum>(enumInt))
            {
                parsedEnum = (TEnum)(object)enumInt!;
                return true;
            }

            return false;
        }
    }
}
