namespace RT.Comb
{
    public static class EnsureOrderedProvider
    {
        private readonly static CustomNoRepeatTimestampProvider SqlNoDupeProvider = new CustomNoRepeatTimestampProvider(4);
        private readonly static CustomNoRepeatTimestampProvider UnixNoDupeProvider = new CustomNoRepeatTimestampProvider(1);
        public readonly static ICombProvider Legacy = new SqlCombProvider(new SqlDateTimeStrategy(), customTimestampProvider: SqlNoDupeProvider.GetTimestamp);
        public readonly static ICombProvider Sql = new SqlCombProvider(new UnixDateTimeStrategy(), customTimestampProvider: UnixNoDupeProvider.GetTimestamp);
        public readonly static ICombProvider PostgreSql = new PostgreSqlCombProvider(new UnixDateTimeStrategy(), customTimestampProvider: UnixNoDupeProvider.GetTimestamp);
    }

    public sealed class CustomNoRepeatTimestampProvider
    {
        private DateTime lastValue = DateTime.MinValue;
        private readonly object locker = new object();

        // By default, increment any subsequent requests by 4ms, which overcomes the resolution of 1/300s of SqlDateTimeStrategy
        // If using UnixDateTimeStrategy, you can set this to as low as 1ms.
        public double IncrementMs { get; set; }

        public CustomNoRepeatTimestampProvider(double incrementMs = 4)
        {
            IncrementMs = incrementMs;
        }

        public DateTime GetTimestamp()
        {
            DateTime now = DateTime.UtcNow;

            lock (locker)
            {
                // Ensure the time difference between the last value and this one is at least the increment threshold
                if ((now - lastValue).TotalMilliseconds < IncrementMs)
                {
                    // now is too close to the last value, use the value with minimum distance from lastValue
                    now = lastValue.AddMilliseconds(IncrementMs);
                }
                lastValue = now;
            }

            return now;
        }
    }
}
