using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace TarkovPerformanceSuite.Core;

/// <summary>Caches process, SPT, EFT, and optional-mod information used in reports and compatibility checks.</summary>
internal sealed class RuntimeInformation
{
    internal string EftVersion { get; private set; }
    internal string UnityVersion { get; private set; }
    internal string BepInExVersion { get; private set; }
    internal string SptVersion { get; private set; }
    internal string FikaVersion { get; private set; }
    internal bool FikaDetected
    {
        get { return !string.IsNullOrEmpty(FikaVersion); }
    }

    internal static RuntimeInformation Detect()
    {
        var info = new RuntimeInformation
        {
            EftVersion = SafeFileVersion(Application.dataPath.Replace("_Data", ".exe")),
            UnityVersion = Application.unityVersion,
            BepInExVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            SptVersion = FindPluginVersion("SPT.Core", "com.spt.core"),
            FikaVersion = FindPluginVersion("Fika.Core", "com.fika.core"),
        };
        if (string.IsNullOrEmpty(info.EftVersion))
        {
            info.EftVersion = Application.version;
        }

        if (string.IsNullOrEmpty(info.SptVersion))
        {
            info.SptVersion = "not detected";
        }

        return info;
    }

    internal void Log(ManualLogSource logger)
    {
        logger.LogInfo(
            $"Environment: EFT {EftVersion}; Unity {UnityVersion}; SPT {SptVersion}; BepInEx {BepInExVersion}; Fika {(FikaDetected ? FikaVersion : "not installed/detected")}"
        );
    }

    private static string FindPluginVersion(string name, string guid)
    {
        foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
        {
            BepInPlugin metadata = pair.Value.Metadata;
            if (
                string.Equals(metadata.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(metadata.GUID, guid, StringComparison.OrdinalIgnoreCase)
            )
            {
                return metadata.Version?.ToString();
            }
        }
        return null;
    }

    private static string SafeFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Retains a bounded list of recent feature failures for diagnostics without flooding the game log.</summary>
internal sealed class RecentExceptionLog
{
    private readonly string[] _entries = new string[16];
    private int _next;
    private int _count;

    internal void Add(string feature, Exception exception, UnityEngine.Object context = null)
    {
        string identity = context == null ? "n/a" : context.name + "#" + context.GetInstanceID();
        _entries[_next] = DateTime.UtcNow.ToString("O") + " | " + feature + " | " + identity + " | " + exception;
        _next = (_next + 1) % _entries.Length;
        if (_count < _entries.Length)
        {
            _count++;
        }
    }

    internal void AppendTo(System.Text.StringBuilder builder)
    {
        if (_count == 0)
        {
            builder.AppendLine("(none)");
            return;
        }
        int start = (_next - _count + _entries.Length) % _entries.Length;
        for (int i = 0; i < _count; i++)
        {
            builder.AppendLine(_entries[(start + i) % _entries.Length]);
        }
    }
}
