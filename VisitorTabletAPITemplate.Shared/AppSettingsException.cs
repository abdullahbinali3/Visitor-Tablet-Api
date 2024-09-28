namespace VisitorTabletAPITemplate
{
    public sealed class AppSettingsException : Exception
    {
        public AppSettingsException()
        {
        }

        public AppSettingsException(string message)
            : base(message)
        {
        }

        public AppSettingsException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
