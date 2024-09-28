namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum UserEnableTotpResult
    {
        UnknownError,
        Ok,
        UserInvalid,
        TotpSecretNotSet,
        TotpCodeInvalid,
        TotpAlreadyEnabled
    }
}
