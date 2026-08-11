using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// DataProtectionのテスト用スタブ（簡易Base64）
/// </summary>
public sealed class FakeDataProtectionProvider : IDataProtectionProvider, IDataProtector
{
    private const string Prefix = "FAKE:";

    public IDataProtector CreateProtector(string purpose)
    {
        return this;
    }

    public IDataProtector CreateProtector(string purpose, params string[] subPurposes)
    {
        return this;
    }

    public string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public string Unprotect(string protectedData)
    {
        if (protectedData.StartsWith(Prefix, StringComparison.Ordinal))
        {
            protectedData = protectedData[Prefix.Length..];
        }
        var bytes = Convert.FromBase64String(protectedData);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] Protect(byte[] plaintext)
    {
        var base64 = Convert.ToBase64String(plaintext);
        return Encoding.UTF8.GetBytes(Prefix + base64);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        var text = Encoding.UTF8.GetString(protectedData);
        if (text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            text = text[Prefix.Length..];
        }
        return Convert.FromBase64String(text);
    }
}
