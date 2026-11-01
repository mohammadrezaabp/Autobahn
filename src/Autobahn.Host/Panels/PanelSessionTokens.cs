using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Autobahn.Host.Panels;

/// <summary>
/// Issues and checks the value of the panel session cookie: an expiry time and an HMAC over it.
/// </summary>
/// <remarks>
/// The console authenticates with an <c>X-API-Key</c> header it adds to each call, and a browser
/// tab opened on Grafana cannot add one. The console therefore trades its key for this cookie,
/// which the panel routes accept and nothing else does — <c>/admin/api</c> still reads only the
/// header, so no control-plane call ever rides on an ambient credential.
///
/// The signing key is random per process. A restart ends every panel session, and the console
/// opens a new one on the next click; nothing has to be stored or shared between replicas.
/// </remarks>
internal sealed class PanelSessionTokens(TimeProvider time)
{
    public const string CookieName = "Autobahn_panels";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Issue(TimeSpan lifetime)
    {
        var expires = time.GetUtcNow().Add(lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"{expires}.{Sign(expires)}";
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator <= 0)
        {
            return false;
        }

        var expires = token[..separator];
        var signed = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(token[(separator + 1)..]),
            Encoding.ASCII.GetBytes(Sign(expires)));

        return signed &&
               long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
               seconds > time.GetUtcNow().ToUnixTimeSeconds();
    }

    private string Sign(string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload)));
}
