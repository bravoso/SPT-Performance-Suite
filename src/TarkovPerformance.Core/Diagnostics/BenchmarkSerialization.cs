using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TarkovPerformanceSuite.Diagnostics;

/// <summary>Represents one immutable frame sample captured during a benchmark window.</summary>
public struct BenchmarkSample
{
    public double TimestampSeconds;
    public double FrameTimeMs;
    public double Fps;
    public double? MainThreadMs;
    public double? RenderThreadMs;
    public double? CpuTotalMs;
    public double? GpuFrameMs;
    public double? FrameTimeGpuMs;
    public double? GfxWaitForPresentMs;
    public double? PlayerLoopMs;
    public double? WaitForTargetFpsMs;
    public double? GcCollectMs;
    public long? GcValue;
    public long? GcUsedMemory;
    public long? GcReservedMemory;
    public long? AppResidentMemory;
    public long? DrawCalls;
    public long? SetPassCalls;
    public int PlayerCount;
    public int AiCount;
    public int VisibleAiCount;
    public int BakedCullingEntityCount;
    public int BakedHiddenEntityCount;
    public int CorpseCount;
    public int AnimatorCount;
    public int SkinnedRendererCount;
    public int ShadowRendererCount;
    public double ShadowEffectiveDistance;
    public int ShadowDisabledRendererCount;
    public int SkinningModifiedRendererCount;
    public int RemoteLodMidAiCount;
    public int RemoteLodFarAiCount;
    public int RemoteLodForcedGroupCount;
    public int RemoteLodModifiedRendererCount;
    public int DeclutterHiddenRendererCount;
    public long AreaLightReusedCommandBuffers;
    public long AreaLightRebuiltCommandBuffers;
    public int RemoteBudgetedCharacterCount;
    public int RemoteCulledAnimatorCount;
    public long RemoteSkippedPropUpdates;
    public long RemoteSkippedTriggerSearches;
    public long RemoteSkippedPresentationUpdates;
    public long RemoteCombatShots;
    public long RemoteSoundOnlyShots;
    public long RemoteCombatSafetyBypasses;
    public long RemoteCulledMuzzleEffects;
    public long RemoteCulledImpactEffects;
    public long RemoteCulledCasings;
    public int RemoteCulledLights;
    public bool RemoteSoundOnlyAuthority;
    public double RemoteCombatDecisionMs;
    public bool OptimizationsEnabled;
    public bool PipScopeActive;
    public bool PipRenderingDisabled;
    public int PipScopeSourceResolution;
    public int PipScopeOptimizedResolution;
    public long PipScopeRenderedFrames;
    public long PipScopeReusedFrames;
    public double PipScopeAverageRenderMs;
    public long CompatibilityFastWorldLookups;
    public int? FikaServerFps;
}

/// <summary>Contains benchmark metadata and the samples written to CSV or JSON.</summary>
public sealed class BenchmarkExport
{
    public string StartedUtc { get; set; }
    public string MapName { get; set; }
    public string EnabledFeatures { get; set; }
    public BenchmarkSample[] Samples { get; set; }
    public int SampleCount { get; set; }
}

/// <summary>Serializes benchmark data without depending on Unity or EFT assemblies.</summary>
public static class BenchmarkSerializer
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void WriteCsv(TextWriter writer, BenchmarkExport export)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (export == null)
        {
            throw new ArgumentNullException(nameof(export));
        }

        writer.WriteLine(
            "timestamp,frame_time_ms,fps,main_thread_ms,render_thread_ms,cpu_total_ms,gpu_frame_ms,frame_time_gpu_ms,gfx_wait_for_present_ms,player_loop_ms,wait_for_target_fps_ms,gc_collect_ms,gc_value,gc_used_memory,gc_reserved_memory,app_resident_memory,draw_calls,setpass_calls,player_count,ai_count,visible_ai_count,baked_culling_entity_count,baked_hidden_entity_count,corpse_count,animator_count,skinned_renderer_count,shadow_renderer_count,shadow_effective_distance,shadow_disabled_renderer_count,skinning_modified_renderer_count,remote_lod_mid_ai_count,remote_lod_far_ai_count,remote_lod_forced_group_count,remote_lod_modified_renderer_count,declutter_hidden_renderer_count,area_light_reused_command_buffers,area_light_rebuilt_command_buffers,remote_budgeted_character_count,remote_culled_animator_count,remote_skipped_prop_updates,remote_skipped_trigger_searches,remote_skipped_presentation_updates,remote_combat_shots,remote_sound_only_shots,remote_combat_safety_bypasses,remote_culled_muzzle_effects,remote_culled_impact_effects,remote_culled_casings,remote_culled_lights,remote_sound_only_authority,remote_combat_decision_ms,optimizations_enabled,pip_scope_active,pip_rendering_disabled,pip_scope_source_resolution,pip_scope_optimized_resolution,pip_scope_rendered_frames,pip_scope_reused_frames,pip_scope_average_render_ms,compatibility_fast_world_lookups,fika_server_fps,enabled_features"
        );
        BenchmarkSample[] samples = export.Samples ?? Array.Empty<BenchmarkSample>();
        int sampleCount = EffectiveSampleCount(export, samples);
        for (int i = 0; i < sampleCount; i++)
        {
            BenchmarkSample s = samples[i];
            WriteDouble(writer, s.TimestampSeconds);
            writer.Write(',');
            WriteDouble(writer, s.FrameTimeMs);
            writer.Write(',');
            WriteDouble(writer, s.Fps);
            writer.Write(',');
            WriteNullableDouble(writer, s.MainThreadMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.RenderThreadMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.CpuTotalMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.GpuFrameMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.FrameTimeGpuMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.GfxWaitForPresentMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.PlayerLoopMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.WaitForTargetFpsMs);
            writer.Write(',');
            WriteNullableDouble(writer, s.GcCollectMs);
            writer.Write(',');
            WriteNullableLong(writer, s.GcValue);
            writer.Write(',');
            WriteNullableLong(writer, s.GcUsedMemory);
            writer.Write(',');
            WriteNullableLong(writer, s.GcReservedMemory);
            writer.Write(',');
            WriteNullableLong(writer, s.AppResidentMemory);
            writer.Write(',');
            WriteNullableLong(writer, s.DrawCalls);
            writer.Write(',');
            WriteNullableLong(writer, s.SetPassCalls);
            writer.Write(',');
            WriteInt(writer, s.PlayerCount);
            writer.Write(',');
            WriteInt(writer, s.AiCount);
            writer.Write(',');
            WriteInt(writer, s.VisibleAiCount);
            writer.Write(',');
            WriteInt(writer, s.BakedCullingEntityCount);
            writer.Write(',');
            WriteInt(writer, s.BakedHiddenEntityCount);
            writer.Write(',');
            WriteInt(writer, s.CorpseCount);
            writer.Write(',');
            WriteInt(writer, s.AnimatorCount);
            writer.Write(',');
            WriteInt(writer, s.SkinnedRendererCount);
            writer.Write(',');
            WriteInt(writer, s.ShadowRendererCount);
            writer.Write(',');
            WriteDouble(writer, s.ShadowEffectiveDistance);
            writer.Write(',');
            WriteInt(writer, s.ShadowDisabledRendererCount);
            writer.Write(',');
            WriteInt(writer, s.SkinningModifiedRendererCount);
            writer.Write(',');
            WriteInt(writer, s.RemoteLodMidAiCount);
            writer.Write(',');
            WriteInt(writer, s.RemoteLodFarAiCount);
            writer.Write(',');
            WriteInt(writer, s.RemoteLodForcedGroupCount);
            writer.Write(',');
            WriteInt(writer, s.RemoteLodModifiedRendererCount);
            writer.Write(',');
            WriteInt(writer, s.DeclutterHiddenRendererCount);
            writer.Write(',');
            WriteLong(writer, s.AreaLightReusedCommandBuffers);
            writer.Write(',');
            WriteLong(writer, s.AreaLightRebuiltCommandBuffers);
            writer.Write(',');
            WriteInt(writer, s.RemoteBudgetedCharacterCount);
            writer.Write(',');
            WriteInt(writer, s.RemoteCulledAnimatorCount);
            writer.Write(',');
            WriteLong(writer, s.RemoteSkippedPropUpdates);
            writer.Write(',');
            WriteLong(writer, s.RemoteSkippedTriggerSearches);
            writer.Write(',');
            WriteLong(writer, s.RemoteSkippedPresentationUpdates);
            writer.Write(',');
            WriteLong(writer, s.RemoteCombatShots);
            writer.Write(',');
            WriteLong(writer, s.RemoteSoundOnlyShots);
            writer.Write(',');
            WriteLong(writer, s.RemoteCombatSafetyBypasses);
            writer.Write(',');
            WriteLong(writer, s.RemoteCulledMuzzleEffects);
            writer.Write(',');
            WriteLong(writer, s.RemoteCulledImpactEffects);
            writer.Write(',');
            WriteLong(writer, s.RemoteCulledCasings);
            writer.Write(',');
            WriteInt(writer, s.RemoteCulledLights);
            writer.Write(',');
            writer.Write(s.RemoteSoundOnlyAuthority ? "true" : "false");
            writer.Write(',');
            WriteDouble(writer, s.RemoteCombatDecisionMs);
            writer.Write(',');
            writer.Write(s.OptimizationsEnabled ? "true" : "false");
            writer.Write(',');
            writer.Write(s.PipScopeActive ? "true" : "false");
            writer.Write(',');
            writer.Write(s.PipRenderingDisabled ? "true" : "false");
            writer.Write(',');
            WriteInt(writer, s.PipScopeSourceResolution);
            writer.Write(',');
            WriteInt(writer, s.PipScopeOptimizedResolution);
            writer.Write(',');
            WriteLong(writer, s.PipScopeRenderedFrames);
            writer.Write(',');
            WriteLong(writer, s.PipScopeReusedFrames);
            writer.Write(',');
            WriteDouble(writer, s.PipScopeAverageRenderMs);
            writer.Write(',');
            WriteLong(writer, s.CompatibilityFastWorldLookups);
            writer.Write(',');
            if (s.FikaServerFps.HasValue)
            {
                WriteInt(writer, s.FikaServerFps.Value);
            }

            writer.Write(',');
            // This metadata used to be repeated on every frame, bloating each file by
            // several megabytes. One copy is enough for CSV consumers.
            if (i == 0)
            {
                WriteCsvField(writer, export.EnabledFeatures ?? string.Empty);
            }

            writer.WriteLine();
        }
    }

    public static void WriteJson(TextWriter writer, BenchmarkExport export)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (export == null)
        {
            throw new ArgumentNullException(nameof(export));
        }

        writer.Write("{\"startedUtc\":");
        WriteJsonString(writer, export.StartedUtc ?? string.Empty);
        writer.Write(",\"mapName\":");
        WriteJsonString(writer, export.MapName ?? string.Empty);
        writer.Write(",\"enabledFeatures\":");
        WriteJsonString(writer, export.EnabledFeatures ?? string.Empty);
        writer.Write(",\"samples\":[");
        BenchmarkSample[] samples = export.Samples ?? Array.Empty<BenchmarkSample>();
        int sampleCount = EffectiveSampleCount(export, samples);
        for (int i = 0; i < sampleCount; i++)
        {
            if (i != 0)
            {
                writer.Write(',');
            }

            BenchmarkSample s = samples[i];
            writer.Write("{\"timestamp\":");
            WriteDouble(writer, s.TimestampSeconds);
            writer.Write(",\"frameTimeMs\":");
            WriteDouble(writer, s.FrameTimeMs);
            writer.Write(",\"fps\":");
            WriteDouble(writer, s.Fps);
            writer.Write(",\"mainThreadMs\":");
            WriteJsonNullable(writer, s.MainThreadMs);
            writer.Write(",\"renderThreadMs\":");
            WriteJsonNullable(writer, s.RenderThreadMs);
            writer.Write(",\"cpuTotalMs\":");
            WriteJsonNullable(writer, s.CpuTotalMs);
            writer.Write(",\"gpuFrameMs\":");
            WriteJsonNullable(writer, s.GpuFrameMs);
            writer.Write(",\"frameTimeGpuMs\":");
            WriteJsonNullable(writer, s.FrameTimeGpuMs);
            writer.Write(",\"gfxWaitForPresentMs\":");
            WriteJsonNullable(writer, s.GfxWaitForPresentMs);
            writer.Write(",\"playerLoopMs\":");
            WriteJsonNullable(writer, s.PlayerLoopMs);
            writer.Write(",\"waitForTargetFpsMs\":");
            WriteJsonNullable(writer, s.WaitForTargetFpsMs);
            writer.Write(",\"gcCollectMs\":");
            WriteJsonNullable(writer, s.GcCollectMs);
            writer.Write(",\"gcValue\":");
            WriteJsonNullable(writer, s.GcValue);
            writer.Write(",\"gcUsedMemory\":");
            WriteJsonNullable(writer, s.GcUsedMemory);
            writer.Write(",\"gcReservedMemory\":");
            WriteJsonNullable(writer, s.GcReservedMemory);
            writer.Write(",\"appResidentMemory\":");
            WriteJsonNullable(writer, s.AppResidentMemory);
            writer.Write(",\"drawCalls\":");
            WriteJsonNullable(writer, s.DrawCalls);
            writer.Write(",\"setPassCalls\":");
            WriteJsonNullable(writer, s.SetPassCalls);
            writer.Write(",\"playerCount\":");
            WriteInt(writer, s.PlayerCount);
            writer.Write(",\"aiCount\":");
            WriteInt(writer, s.AiCount);
            writer.Write(",\"visibleAiCount\":");
            WriteInt(writer, s.VisibleAiCount);
            writer.Write(",\"bakedCullingEntityCount\":");
            WriteInt(writer, s.BakedCullingEntityCount);
            writer.Write(",\"bakedHiddenEntityCount\":");
            WriteInt(writer, s.BakedHiddenEntityCount);
            writer.Write(",\"corpseCount\":");
            WriteInt(writer, s.CorpseCount);
            writer.Write(",\"animatorCount\":");
            WriteInt(writer, s.AnimatorCount);
            writer.Write(",\"skinnedRendererCount\":");
            WriteInt(writer, s.SkinnedRendererCount);
            writer.Write(",\"shadowRendererCount\":");
            WriteInt(writer, s.ShadowRendererCount);
            writer.Write(",\"shadowEffectiveDistance\":");
            WriteDouble(writer, s.ShadowEffectiveDistance);
            writer.Write(",\"shadowDisabledRendererCount\":");
            WriteInt(writer, s.ShadowDisabledRendererCount);
            writer.Write(",\"skinningModifiedRendererCount\":");
            WriteInt(writer, s.SkinningModifiedRendererCount);
            writer.Write(",\"remoteLodMidAiCount\":");
            WriteInt(writer, s.RemoteLodMidAiCount);
            writer.Write(",\"remoteLodFarAiCount\":");
            WriteInt(writer, s.RemoteLodFarAiCount);
            writer.Write(",\"remoteLodForcedGroupCount\":");
            WriteInt(writer, s.RemoteLodForcedGroupCount);
            writer.Write(",\"remoteLodModifiedRendererCount\":");
            WriteInt(writer, s.RemoteLodModifiedRendererCount);
            writer.Write(",\"declutterHiddenRendererCount\":");
            WriteInt(writer, s.DeclutterHiddenRendererCount);
            writer.Write(",\"areaLightReusedCommandBuffers\":");
            WriteLong(writer, s.AreaLightReusedCommandBuffers);
            writer.Write(",\"areaLightRebuiltCommandBuffers\":");
            WriteLong(writer, s.AreaLightRebuiltCommandBuffers);
            writer.Write(",\"remoteBudgetedCharacterCount\":");
            WriteInt(writer, s.RemoteBudgetedCharacterCount);
            writer.Write(",\"remoteCulledAnimatorCount\":");
            WriteInt(writer, s.RemoteCulledAnimatorCount);
            writer.Write(",\"remoteSkippedPropUpdates\":");
            WriteLong(writer, s.RemoteSkippedPropUpdates);
            writer.Write(",\"remoteSkippedTriggerSearches\":");
            WriteLong(writer, s.RemoteSkippedTriggerSearches);
            writer.Write(",\"remoteSkippedPresentationUpdates\":");
            WriteLong(writer, s.RemoteSkippedPresentationUpdates);
            writer.Write(",\"remoteCombatShots\":");
            WriteLong(writer, s.RemoteCombatShots);
            writer.Write(",\"remoteSoundOnlyShots\":");
            WriteLong(writer, s.RemoteSoundOnlyShots);
            writer.Write(",\"remoteCombatSafetyBypasses\":");
            WriteLong(writer, s.RemoteCombatSafetyBypasses);
            writer.Write(",\"remoteCulledMuzzleEffects\":");
            WriteLong(writer, s.RemoteCulledMuzzleEffects);
            writer.Write(",\"remoteCulledImpactEffects\":");
            WriteLong(writer, s.RemoteCulledImpactEffects);
            writer.Write(",\"remoteCulledCasings\":");
            WriteLong(writer, s.RemoteCulledCasings);
            writer.Write(",\"remoteCulledLights\":");
            WriteInt(writer, s.RemoteCulledLights);
            writer.Write(",\"remoteSoundOnlyAuthority\":");
            writer.Write(s.RemoteSoundOnlyAuthority ? "true" : "false");
            writer.Write(",\"remoteCombatDecisionMs\":");
            WriteDouble(writer, s.RemoteCombatDecisionMs);
            writer.Write(",\"optimizationsEnabled\":");
            writer.Write(s.OptimizationsEnabled ? "true" : "false");
            writer.Write(",\"pipScopeActive\":");
            writer.Write(s.PipScopeActive ? "true" : "false");
            writer.Write(",\"pipRenderingDisabled\":");
            writer.Write(s.PipRenderingDisabled ? "true" : "false");
            writer.Write(",\"pipScopeSourceResolution\":");
            WriteInt(writer, s.PipScopeSourceResolution);
            writer.Write(",\"pipScopeOptimizedResolution\":");
            WriteInt(writer, s.PipScopeOptimizedResolution);
            writer.Write(",\"pipScopeRenderedFrames\":");
            WriteLong(writer, s.PipScopeRenderedFrames);
            writer.Write(",\"pipScopeReusedFrames\":");
            WriteLong(writer, s.PipScopeReusedFrames);
            writer.Write(",\"pipScopeAverageRenderMs\":");
            WriteDouble(writer, s.PipScopeAverageRenderMs);
            writer.Write(",\"compatibilityFastWorldLookups\":");
            WriteLong(writer, s.CompatibilityFastWorldLookups);
            writer.Write(",\"fikaServerFps\":");
            WriteJsonNullable(writer, s.FikaServerFps);
            writer.Write('}');
        }
        writer.Write("]}");
    }

    private static int EffectiveSampleCount(BenchmarkExport export, BenchmarkSample[] samples)
    {
        int count = export.SampleCount <= 0 ? samples.Length : export.SampleCount;
        return count > samples.Length ? samples.Length : count;
    }

    private static void WriteDouble(TextWriter writer, double value)
    {
        Span<char> buffer = stackalloc char[64];
        if (!value.TryFormat(buffer, out int written, "0.######", Invariant))
        {
            throw new InvalidOperationException("Unable to format benchmark value.");
        }

        writer.Write(buffer.Slice(0, written));
    }

    private static void WriteInt(TextWriter writer, int value)
    {
        Span<char> buffer = stackalloc char[16];
        if (!value.TryFormat(buffer, out int written, default, Invariant))
        {
            throw new InvalidOperationException("Unable to format benchmark integer.");
        }

        writer.Write(buffer.Slice(0, written));
    }

    private static void WriteLong(TextWriter writer, long value)
    {
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out int written, default, Invariant))
        {
            throw new InvalidOperationException("Unable to format benchmark integer.");
        }

        writer.Write(buffer.Slice(0, written));
    }

    private static void WriteNullableDouble(TextWriter writer, double? value)
    {
        if (value.HasValue)
        {
            WriteDouble(writer, value.Value);
        }
    }

    private static void WriteNullableLong(TextWriter writer, long? value)
    {
        if (value.HasValue)
        {
            WriteLong(writer, value.Value);
        }
    }

    private static void WriteJsonNullable(TextWriter writer, double? value)
    {
        if (value.HasValue)
        {
            WriteDouble(writer, value.Value);
        }
        else
        {
            writer.Write("null");
        }
    }

    private static void WriteJsonNullable(TextWriter writer, long? value)
    {
        if (value.HasValue)
        {
            WriteLong(writer, value.Value);
        }
        else
        {
            writer.Write("null");
        }
    }

    private static void WriteJsonNullable(TextWriter writer, int? value)
    {
        if (value.HasValue)
        {
            WriteInt(writer, value.Value);
        }
        else
        {
            writer.Write("null");
        }
    }

    private static void WriteCsvField(TextWriter writer, string value)
    {
        writer.Write('"');
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                writer.Write('"');
            }

            writer.Write(value[i]);
        }
        writer.Write('"');
    }

    private static void WriteJsonString(TextWriter writer, string value)
    {
        writer.Write('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"':
                    writer.Write("\\\"");
                    break;
                case '\\':
                    writer.Write("\\\\");
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                case '\r':
                    writer.Write("\\r");
                    break;
                case '\t':
                    writer.Write("\\t");
                    break;
                default:
                    if (c < 32)
                    {
                        writer.Write("\\u" + ((int)c).ToString("x4"));
                    }
                    else
                    {
                        writer.Write(c);
                    }

                    break;
            }
        }
        writer.Write('"');
    }
}
