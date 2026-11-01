namespace Autobahn.Application.Security;
public static class ApiKeyValidationFailureExtensions
{
    /// <summary>
    /// True when the presented key matched a stored record that has stopped working, as opposed to
    /// one the gateway has never issued.
    /// </summary>
    /// <remarks>
    /// This is the line between "your credential was withdrawn" and "that is not one of our keys",
    /// and only the gateway can tell them apart — both look identical to the caller. Routes that
    /// serve anonymous callers use it to decide whether an unusable key may be ignored: an
    /// unrecognised key grants nothing a bare request would not already get, so it is treated as no
    /// key at all, while a revoked, expired, or deactivated-tenant key must fail loudly rather than
    /// answer 200 and leave its holder believing the credential still works.
    /// </remarks>
    public static bool IsRecognizedCredential(this ApiKeyValidationFailure failure) => failure is ApiKeyValidationFailure.Expired or ApiKeyValidationFailure.Revoked or ApiKeyValidationFailure.TenantInactive;
}
