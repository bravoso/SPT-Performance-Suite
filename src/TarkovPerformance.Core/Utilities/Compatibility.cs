using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace TarkovPerformanceSuite.Utilities;

/// <summary>Performs dependency version checks without loading optional mod assemblies directly.</summary>
public static class VersionTools
{
    public static int Compare(string left, string right)
    {
        Version a = Parse(left);
        Version b = Parse(right);
        return a.CompareTo(b);
    }

    public static Version Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Version(0, 0);
        }

        int end = 0;
        while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.'))
        {
            end++;
        }

        string clean = value.Substring(0, end).TrimEnd('.');
        return Version.TryParse(clean, out Version result) ? result : new Version(0, 0);
    }
}

/// <summary>Builds stable method fingerprints used to reject patches when a game update changes a target.</summary>
public static class MethodSignatureFingerprint
{
    public static string Describe(MethodBase method)
    {
        if (method == null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        var builder = new StringBuilder(128);
        builder.Append(method.DeclaringType?.FullName).Append("::").Append(method.Name).Append('(');
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i != 0)
            {
                builder.Append(',');
            }

            builder.Append(parameters[i].ParameterType.FullName);
        }
        builder.Append(')');
        if (method is MethodInfo info)
        {
            builder.Append("->").Append(info.ReturnType.FullName);
        }

        return builder.ToString();
    }

    public static string Sha256(MethodBase method)
    {
        string description = Describe(method);
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(description));
            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
