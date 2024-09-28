namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum UserSelfRegistrationResult
    {
        UnknownError,
        Ok,
        RecordAlreadyExists,
        EmailDomainDoesNotBelongToAnExistingOrganization,
        BuildingIdOrFunctionIdDoesNotBelongToMatchedOrganization,
        LocalLoginDisabled,
        SingleSignOnNotEnabled,
        RegisterTokenInvalid,
        GetAppLockFailed
    }
}
