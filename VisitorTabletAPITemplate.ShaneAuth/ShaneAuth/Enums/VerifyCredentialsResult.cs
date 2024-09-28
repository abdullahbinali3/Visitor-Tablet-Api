namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum VerifyCredentialsResult
    {
        UnknownError,
        Ok,
        UserDidNotExist,
        PasswordInvalid,
        PasswordNotSet,
        PasswordLoginLockedOut,
        TotpCodeRequired,
        TotpLockedOut,
        TotpCodeInvalid,
        TotpCodeAlreadyUsed,
        TotpAlreadyEnabled,
        NoAccess
    }
}
