namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum UserDisableTotpResult
    {
        UnknownError,
        Ok,
        UserInvalid,
        TotpNotEnabled,
        PasswordInvalid,
        DisableTotpTokenInvalid
    }
}
