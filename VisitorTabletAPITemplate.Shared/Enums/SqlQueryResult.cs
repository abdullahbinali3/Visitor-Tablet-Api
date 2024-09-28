namespace VisitorTabletAPITemplate.Enums
{
    public enum SqlQueryResult
    {
        UnknownError,
        Ok,
        RecordDidNotExist,
        RecordAlreadyExists,
        RecordInvalid,
        RecordIsInUse,
        SubRecordDidNotExist,
        SubRecordAlreadyExists,
        SubRecordInvalid,
        SubRecordNotInUse,
        ConcurrencyKeyInvalid,
        InsufficientPermissions,
        InvalidDeleteConfirmation,
        DependentApiError,
        InvalidFileType,
        GetAppLockFailed,
        NoValueIsSet
    }
}
