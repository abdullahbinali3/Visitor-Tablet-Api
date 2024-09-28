namespace VisitorTabletAPITemplate.Utilities
{
    public static partial class Toolbox
    {
        public static bool IsValidLatitude(float latitude)
        {
            if (latitude >= -90f && latitude <= 90f)
                return true;

            return false;
        }

        public static bool IsValidLongtitude(float longtitude)
        {
            if (longtitude >= -180f && longtitude <= 180f)
                return true;

            return false;
        }

        /// <summary>
        /// Takes a timezone string and a <see cref="DateTime"/> for a moment in time in UTC, and returns a <see cref="DateTime"/> for the same moment in time in the specified timezone's local time.
        /// </summary>
        /// <param name="timezone"></param>
        /// <param name="dateTimeUtc"></param>
        /// <returns></returns>
        public static DateTime GetDateTimeLocal(string timezone, DateTime dateTimeUtc)
        {
            TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return GetDateTimeLocal(timeZoneInfo, dateTimeUtc);
        }

        /// <summary>
        /// Takes a <see cref="TimeZoneInfo"/> and a <see cref="DateTime"/> for a moment in time in UTC, and returns a <see cref="DateTime"/> for the same moment in time in the specified timezone's local time.
        /// </summary>
        /// <param name="timeZoneInfo"></param>
        /// <param name="dateTimeUtc"></param>
        /// <returns></returns>
        public static DateTime GetDateTimeLocal(TimeZoneInfo timeZoneInfo, DateTime dateTimeUtc)
        {
            if (dateTimeUtc.Kind == DateTimeKind.Utc)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtc, timeZoneInfo);
            }
            else
            {
                // Specify DateTimeKind for input date if not UTC before doing conversion
                DateTime dateTimeUtcWithKind = DateTime.SpecifyKind(dateTimeUtc, DateTimeKind.Utc);
                return TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtcWithKind, timeZoneInfo);
            }
        }

        /// <summary>
        /// Takes a timezone string and a <see cref="DateTime"/> for a moment in time in UTC, and returns <see cref="DateTime"/>s for the start/end time for midnight to midnight for that day in the specified timezone in UTC.
        /// </summary>
        /// <param name="timezone"></param>
        /// <param name="dateTimeUtc"></param>
        /// <returns></returns>
        public static (DateTime dayStartTimeUtc, DateTime dayEndTimeUtc) GetDayStartEndUtcTime(string timezone, DateTime dateTimeUtc)
        {
            TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return GetDayStartEndUtcTime(timeZoneInfo, dateTimeUtc);
        }

        /// <summary>
        /// Takes a <see cref="TimeZoneInfo"/> and a <see cref="DateTime"/> for a moment in time in UTC, and returns <see cref="DateTime"/>s for the start/end time for midnight to midnight for that day in the specified timezone in UTC.
        /// </summary>
        /// <param name="timeZoneInfo"></param>
        /// <param name="dateTimeUtc"></param>
        /// <returns></returns>
        public static (DateTime dayStartTimeUtc, DateTime dayEndTimeUtc) GetDayStartEndUtcTime(TimeZoneInfo timeZoneInfo, DateTime dateTimeUtc)
        {
            DateTime dateTimeLocal;

            if (dateTimeUtc.Kind == DateTimeKind.Utc)
            {
                dateTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtc, timeZoneInfo);
            }
            else
            {
                // Specify DateTimeKind for input date if not UTC before doing conversion
                DateTime dateTimeUtcWithKind = DateTime.SpecifyKind(dateTimeUtc, DateTimeKind.Utc);
                dateTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtcWithKind, timeZoneInfo);
            }

            DateTime dayStartLocal = new DateTime(dateTimeLocal.Year, dateTimeLocal.Month, dateTimeLocal.Day, 0, 0, 0, DateTimeKind.Unspecified);
            DateTime dayEndLocal = dayStartLocal.AddDays(1);

            DateTime dayStartTimeUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, timeZoneInfo);
            DateTime dayEndTimeUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, timeZoneInfo);

            return (dayStartTimeUtc, dayEndTimeUtc);
        }

        /// <summary>
        /// <para>As Excel seems to automatically adjust column widths slightly when you open the file, this function returns an accurate number that can be used to achieve the column with you want.</para>
        /// <para>Example use: worksheet.Column(1).Width = EPPlusHelper.GetTrueColumnWidth(44.86);</para>
        /// <para>Written by Tom Dierickx in Visual Basic. https://epplus.codeplex.com/workitem/12457 </para>
        /// <para>Converted to C# by grimmdp (Dean) at the same link above.</para>
        /// </summary>
        /// <param name="width">The intended column width to use for an Excel column.</param>
        /// <returns></returns>
        public static double EPPlusGetTrueColumnWidth(double width)
        {
            //DEDUCE WHAT THE COLUMN WIDTH WOULD REALLY GET SET TO
            double z = 1d;
            if (width >= (1 + 2 / 3))
            {
                z = Math.Round((Math.Round(7 * (width - 1 / 256), 0) - 5) / 7, 2);
            }
            else
            {
                z = Math.Round((Math.Round(12 * (width - 1 / 256), 0) - Math.Round(5 * width, 0)) / 12, 2);
            }

            //HOW FAR OFF? (WILL BE LESS THAN 1)
            double errorAmt = width - z;

            //CALCULATE WHAT AMOUNT TO TACK ONTO THE ORIGINAL AMOUNT TO RESULT IN THE CLOSEST POSSIBLE SETTING 
            double adj = 0d;
            if (width >= (1 + 2 / 3))
            {
                adj = (Math.Round(7 * errorAmt - 7 / 256, 0)) / 7;
            }
            else
            {
                adj = ((Math.Round(12 * errorAmt - 12 / 256, 0)) / 12) + (2 / 12);
            }

            //RETURN A SCALED-VALUE THAT SHOULD RESULT IN THE NEAREST POSSIBLE VALUE TO THE TRUE DESIRED SETTING
            if (z > 0)
            {
                return width + adj;
            }

            return 0d;
        }
    }
}
