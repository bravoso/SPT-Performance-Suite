using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.RuntimeFeatures;

namespace TarkovPerformanceSuite.RuntimeDiagnostics;

/// <summary>Builds the human-readable snapshot used for support and controlled performance comparisons.</summary>
internal static class DiagnosticReport
{
    internal static string Write(
        string directory,
        RuntimeInformation runtime,
        ProfilerMetrics metrics,
        EntityCounts counts,
        RecentExceptionLog exceptions,
        string featureState,
        string combatStatus,
        string benchmarkConfiguration,
        string methodPatchReport,
        string methodTimingSnapshot,
        double suiteMs
    )
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "diagnostics_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
        var builder = new StringBuilder(8192);
        builder.AppendLine("Tarkov Performance Suite diagnostics " + Plugin.PluginVersion);
        builder.AppendLine("Generated: " + DateTime.Now.ToString("O"));
        builder.AppendLine();
        builder.AppendLine("VERSIONS");
        builder.AppendLine(
            $"EFT={runtime.EftVersion}; Unity={runtime.UnityVersion}; SPT={runtime.SptVersion}; BepInEx={runtime.BepInExVersion}; Fika={(runtime.FikaDetected ? runtime.FikaVersion : "not detected")}"
        );
        builder.AppendLine();
        builder.AppendLine("LOADED PLUGINS");
        foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
        {
            builder.AppendLine(pair.Value.Metadata.GUID + " | " + pair.Value.Metadata.Name + " | " + pair.Value.Metadata.Version);
        }

        builder.AppendLine();
        builder.AppendLine("LOADED PATCHER FILES");
        try
        {
            string patcherPath = BepInEx.Paths.PatcherPluginPath;
            string[] files = Directory.GetFiles(patcherPath, "*.dll", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                builder.AppendLine(Path.GetFileName(files[i]));
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine("unavailable: " + ex.Message);
        }
        builder.AppendLine();
        builder.AppendLine("TARGET PATCH OWNERS");
        builder.AppendLine(
            string.IsNullOrEmpty(methodPatchReport)
                ? "Method timing disabled; owners will be inspected when targets initialize."
                : methodPatchReport
        );
        builder.AppendLine();
        builder.AppendLine("CURRENT METHOD TIMING SNAPSHOT");
        builder.AppendLine(string.IsNullOrEmpty(methodTimingSnapshot) ? "unavailable" : methodTimingSnapshot);
        builder.AppendLine();
        builder.AppendLine("ACTIVE FEATURE STATES");
        builder.AppendLine(featureState);
        builder.AppendLine("remoteCombat=" + combatStatus);
        builder.AppendLine(
            $"entities={counts.Players}; ai={counts.Ai}; visibleAi={counts.VisibleAi}; bakedCullingEntities={counts.BakedCullingEntities}; bakedHiddenEntities={counts.BakedHiddenEntities}; corpses(partial)={counts.Corpses}; animators={counts.Animators}; skinnedRenderers={counts.SkinnedRenderers}; shadowRenderers={counts.ShadowRenderers}"
        );
        builder.AppendLine("suiteAverageMs=" + suiteMs.ToString("F4"));
        builder.AppendLine("benchmarkConfiguration=" + benchmarkConfiguration);
        builder.AppendLine();
        builder.AppendLine("CURRENT PROFILER SNAPSHOT");
        metrics.AppendCurrentSnapshot(builder);
        builder.AppendLine();
        builder.AppendLine("AVAILABLE PROFILER METRICS");
        IReadOnlyList<string> available = metrics.Available;
        for (int i = 0; i < available.Count; i++)
        {
            builder.AppendLine(available[i]);
        }

        builder.AppendLine();
        builder.AppendLine("RECENT EXCEPTIONS");
        exceptions.AppendTo(builder);
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }
}
