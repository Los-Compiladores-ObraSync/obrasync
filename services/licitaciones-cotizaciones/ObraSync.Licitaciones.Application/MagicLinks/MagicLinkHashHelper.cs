using System.Security.Cryptography;
using System.Text;

namespace ObraSync.Licitaciones.Application.MagicLinks;

public static class MagicLinkHashHelper
{
    public static string ComputeSha256(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(token.Trim());
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ComputeSha256Hash(string token) => ComputeSha256(token);
}
