using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TarkovPerformanceSuite.Diagnostics
{
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
        public long? DrawCalls;
        public long? SetPassCalls;
        public int PlayerCount;
        public int AiCount;
        public int VisibleAiCount;
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
        public int RemoteBudgetedCharacterCount;
        public int RemoteCulledAnimatorCount;
        public long RemoteSkippedPropUpdates;
        public long RemoteSkippedTriggerSearches;
        public long CompatibilityFastWorldLookups;
        public int? FikaServerFps;
    }

    public sealed class BenchmarkExport
    {
        public string StartedUtc { get; set; }
        public string MapName { get; set; }
        public string EnabledFeatures { get; set; }
        public BenchmarkSample[] Samples { get; set; }
    }

    public static class BenchmarkSerializer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static void WriteCsv(TextWriter writer, BenchmarkExport export)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (export == null) throw new ArgumentNullException(nameof(export));
            writer.WriteLine("timestamp,frame_time_ms,fps,main_thread_ms,render_thread_ms,cpu_total_ms,gpu_frame_ms,frame_time_gpu_ms,gfx_wait_for_present_ms,player_loop_ms,wait_for_target_fps_ms,gc_collect_ms,gc_value,draw_calls,setpass_calls,player_count,ai_count,visible_ai_count,corpse_count,animator_count,skinned_renderer_count,shadow_renderer_count,shadow_effective_distance,shadow_disabled_renderer_count,skinning_modified_renderer_count,remote_lod_mid_ai_count,remote_lod_far_ai_count,remote_lod_forced_group_count,remote_lod_modified_renderer_count,declutter_hidden_renderer_count,remote_budgeted_character_count,remote_culled_animator_count,remote_skipped_prop_updates,remote_skipped_trigger_searches,compatibility_fast_world_lookups,fika_server_fps,enabled_features");
            BenchmarkSample[] samples = export.Samples ?? Array.Empty<BenchmarkSample>();
            for (int i = 0; i < samples.Length; i++)
            {
                BenchmarkSample s = samples[i];
                WriteDouble(writer, s.TimestampSeconds); writer.Write(',');
                WriteDouble(writer, s.FrameTimeMs); writer.Write(',');
                WriteDouble(writer, s.Fps); writer.Write(',');
                WriteNullableDouble(writer, s.MainThreadMs); writer.Write(',');
                WriteNullableDouble(writer, s.RenderThreadMs); writer.Write(',');
                WriteNullableDouble(writer, s.CpuTotalMs); writer.Write(',');
                WriteNullableDouble(writer, s.GpuFrameMs); writer.Write(',');
                WriteNullableDouble(writer, s.FrameTimeGpuMs); writer.Write(',');
                WriteNullableDouble(writer, s.GfxWaitForPresentMs); writer.Write(',');
                WriteNullableDouble(writer, s.PlayerLoopMs); writer.Write(',');
                WriteNullableDouble(writer, s.WaitForTargetFpsMs); writer.Write(',');
                WriteNullableDouble(writer, s.GcCollectMs); writer.Write(',');
                WriteNullableLong(writer, s.GcValue); writer.Write(',');
                WriteNullableLong(writer, s.DrawCalls); writer.Write(',');
                WriteNullableLong(writer, s.SetPassCalls); writer.Write(',');
                writer.Write(s.PlayerCount); writer.Write(',');
                writer.Write(s.AiCount); writer.Write(',');
                writer.Write(s.VisibleAiCount); writer.Write(',');
                writer.Write(s.CorpseCount); writer.Write(',');
                writer.Write(s.AnimatorCount); writer.Write(',');
                writer.Write(s.SkinnedRendererCount); writer.Write(',');
                writer.Write(s.ShadowRendererCount); writer.Write(',');
                WriteDouble(writer, s.ShadowEffectiveDistance); writer.Write(',');
                writer.Write(s.ShadowDisabledRendererCount); writer.Write(',');
                writer.Write(s.SkinningModifiedRendererCount); writer.Write(',');
                writer.Write(s.RemoteLodMidAiCount); writer.Write(',');
                writer.Write(s.RemoteLodFarAiCount); writer.Write(',');
                writer.Write(s.RemoteLodForcedGroupCount); writer.Write(',');
                writer.Write(s.RemoteLodModifiedRendererCount); writer.Write(',');
                writer.Write(s.DeclutterHiddenRendererCount); writer.Write(',');
                writer.Write(s.RemoteBudgetedCharacterCount); writer.Write(',');
                writer.Write(s.RemoteCulledAnimatorCount); writer.Write(',');
                writer.Write(s.RemoteSkippedPropUpdates); writer.Write(',');
                writer.Write(s.RemoteSkippedTriggerSearches); writer.Write(',');
                writer.Write(s.CompatibilityFastWorldLookups); writer.Write(',');
                if (s.FikaServerFps.HasValue) writer.Write(s.FikaServerFps.Value);
                writer.Write(',');
                WriteCsvField(writer, export.EnabledFeatures ?? string.Empty);
                writer.WriteLine();
            }
        }

        public static void WriteJson(TextWriter writer, BenchmarkExport export)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (export == null) throw new ArgumentNullException(nameof(export));
            writer.Write("{\"startedUtc\":"); WriteJsonString(writer, export.StartedUtc ?? string.Empty);
            writer.Write(",\"mapName\":"); WriteJsonString(writer, export.MapName ?? string.Empty);
            writer.Write(",\"enabledFeatures\":"); WriteJsonString(writer, export.EnabledFeatures ?? string.Empty);
            writer.Write(",\"samples\":[");
            BenchmarkSample[] samples = export.Samples ?? Array.Empty<BenchmarkSample>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (i != 0) writer.Write(',');
                BenchmarkSample s = samples[i];
                writer.Write("{\"timestamp\":"); WriteDouble(writer, s.TimestampSeconds);
                writer.Write(",\"frameTimeMs\":"); WriteDouble(writer, s.FrameTimeMs);
                writer.Write(",\"fps\":"); WriteDouble(writer, s.Fps);
                writer.Write(",\"mainThreadMs\":"); WriteJsonNullable(writer, s.MainThreadMs);
                writer.Write(",\"renderThreadMs\":"); WriteJsonNullable(writer, s.RenderThreadMs);
                writer.Write(",\"cpuTotalMs\":"); WriteJsonNullable(writer, s.CpuTotalMs);
                writer.Write(",\"gpuFrameMs\":"); WriteJsonNullable(writer, s.GpuFrameMs);
                writer.Write(",\"frameTimeGpuMs\":"); WriteJsonNullable(writer, s.FrameTimeGpuMs);
                writer.Write(",\"gfxWaitForPresentMs\":"); WriteJsonNullable(writer, s.GfxWaitForPresentMs);
                writer.Write(",\"playerLoopMs\":"); WriteJsonNullable(writer, s.PlayerLoopMs);
                writer.Write(",\"waitForTargetFpsMs\":"); WriteJsonNullable(writer, s.WaitForTargetFpsMs);
                writer.Write(",\"gcCollectMs\":"); WriteJsonNullable(writer, s.GcCollectMs);
                writer.Write(",\"gcValue\":"); WriteJsonNullable(writer, s.GcValue);
                writer.Write(",\"drawCalls\":"); WriteJsonNullable(writer, s.DrawCalls);
                writer.Write(",\"setPassCalls\":"); WriteJsonNullable(writer, s.SetPassCalls);
                writer.Write(",\"playerCount\":"); writer.Write(s.PlayerCount);
                writer.Write(",\"aiCount\":"); writer.Write(s.AiCount);
                writer.Write(",\"visibleAiCount\":"); writer.Write(s.VisibleAiCount);
                writer.Write(",\"corpseCount\":"); writer.Write(s.CorpseCount);
                writer.Write(",\"animatorCount\":"); writer.Write(s.AnimatorCount);
                writer.Write(",\"skinnedRendererCount\":"); writer.Write(s.SkinnedRendererCount);
                writer.Write(",\"shadowRendererCount\":"); writer.Write(s.ShadowRendererCount);
                writer.Write(",\"shadowEffectiveDistance\":"); WriteDouble(writer, s.ShadowEffectiveDistance);
                writer.Write(",\"shadowDisabledRendererCount\":"); writer.Write(s.ShadowDisabledRendererCount);
                writer.Write(",\"skinningModifiedRendererCount\":"); writer.Write(s.SkinningModifiedRendererCount);
                writer.Write(",\"remoteLodMidAiCount\":"); writer.Write(s.RemoteLodMidAiCount);
                writer.Write(",\"remoteLodFarAiCount\":"); writer.Write(s.RemoteLodFarAiCount);
                writer.Write(",\"remoteLodForcedGroupCount\":"); writer.Write(s.RemoteLodForcedGroupCount);
                writer.Write(",\"remoteLodModifiedRendererCount\":"); writer.Write(s.RemoteLodModifiedRendererCount);
                writer.Write(",\"declutterHiddenRendererCount\":"); writer.Write(s.DeclutterHiddenRendererCount);
                writer.Write(",\"remoteBudgetedCharacterCount\":"); writer.Write(s.RemoteBudgetedCharacterCount);
                writer.Write(",\"remoteCulledAnimatorCount\":"); writer.Write(s.RemoteCulledAnimatorCount);
                writer.Write(",\"remoteSkippedPropUpdates\":"); writer.Write(s.RemoteSkippedPropUpdates);
                writer.Write(",\"remoteSkippedTriggerSearches\":"); writer.Write(s.RemoteSkippedTriggerSearches);
                writer.Write(",\"compatibilityFastWorldLookups\":"); writer.Write(s.CompatibilityFastWorldLookups);
                writer.Write(",\"fikaServerFps\":"); WriteJsonNullable(writer, s.FikaServerFps);
                writer.Write('}');
            }
            writer.Write("]}");
        }

        private static void WriteDouble(TextWriter writer, double value) => writer.Write(value.ToString("0.######", Invariant));
        private static void WriteNullableDouble(TextWriter writer, double? value) { if (value.HasValue) WriteDouble(writer, value.Value); }
        private static void WriteNullableLong(TextWriter writer, long? value) { if (value.HasValue) writer.Write(value.Value); }
        private static void WriteJsonNullable(TextWriter writer, double? value) { if (value.HasValue) WriteDouble(writer, value.Value); else writer.Write("null"); }
        private static void WriteJsonNullable(TextWriter writer, long? value) { if (value.HasValue) writer.Write(value.Value); else writer.Write("null"); }
        private static void WriteJsonNullable(TextWriter writer, int? value) { if (value.HasValue) writer.Write(value.Value); else writer.Write("null"); }

        private static void WriteCsvField(TextWriter writer, string value)
        {
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\""));
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
                    case '"': writer.Write("\\\""); break;
                    case '\\': writer.Write("\\\\"); break;
                    case '\n': writer.Write("\\n"); break;
                    case '\r': writer.Write("\\r"); break;
                    case '\t': writer.Write("\\t"); break;
                    default:
                        if (c < 32) writer.Write("\\u" + ((int)c).ToString("x4"));
                        else writer.Write(c);
                        break;
                }
            }
            writer.Write('"');
        }
    }
}
