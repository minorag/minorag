using System.Security.Cryptography;
using System.Text;

namespace Minorag.Core.Services;

public static class CryptoHelper
{
    public static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}
