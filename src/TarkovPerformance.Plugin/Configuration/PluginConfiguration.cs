using BepInEx.Configuration;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.Configuration
{
    internal sealed class PluginConfiguration
    {
        internal PluginConfiguration(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Master switch for the suite.");
            VerboseLogging = config.Bind("General", "VerboseLogging", false, "Log additional lifecycle details; never logs per frame.");
            OverlayEnabled = config.Bind("Diagnostics", "OverlayEnabled", true, "Show the in-raid diagnostics overlay.");
            OverlayKey = config.Bind("Diagnostics", "OverlayKey", new KeyboardShortcut(KeyCode.Keypad7), "Toggle the diagnostics overlay. Also editable in the F12 Configuration Manager.");
            CaptureKey = config.Bind("Diagnostics", "CaptureKey", new KeyboardShortcut(KeyCode.Keypad8), "Start a benchmark capture. Also editable in the F12 Configuration Manager.");
            DiagnosticReportKey = config.Bind("Diagnostics", "DiagnosticReportKey", new KeyboardShortcut(KeyCode.Keypad9), "Export a diagnostic report with current profiler and method timings.");
            CaptureDurationSeconds = config.Bind("Diagnostics", "CaptureDurationSeconds", 120f, "Capture duration, clamped to 5-900 seconds.");
            ExportCsv = config.Bind("Diagnostics", "ExportCsv", true, "Write CSV in addition to JSON.");
            MethodTimingEnabled = config.Bind("Diagnostics", "MethodTimingEnabled", false, "Enable diagnostics-only Harmony method timing. Can be changed live from the F12 menu.");
            ShadowEnabled = config.Bind("Experiments", "RemoteCharacterShadowsEnabled", false, "Disable distant confirmed remote-AI shadow casting. Disabled by default.");
            ShadowDistance = config.Bind("Experiments", "RemoteCharacterShadowDistance", 120f, "Distance in metres, clamped to 20-1000.");
            ShadowUpdateInterval = config.Bind("Experiments", "RemoteCharacterShadowUpdateIntervalSeconds", 0.25f, "Time-based update interval, clamped to 0.1-5 seconds.");
            ShadowToggleKey = config.Bind("Experiments", "RemoteCharacterShadowToggleKey", new KeyboardShortcut(KeyCode.Keypad6), "Toggle the shadow experiment. The Boolean toggle is also available in the F12 menu.");
            ShadowDryRun = config.Bind("Experiments", "RemoteCharacterShadowsDryRun", false, "Report changes without modifying renderers.");
        }

        internal ConfigEntry<bool> Enabled { get; }
        internal ConfigEntry<bool> VerboseLogging { get; }
        internal ConfigEntry<bool> OverlayEnabled { get; }
        internal ConfigEntry<KeyboardShortcut> OverlayKey { get; }
        internal ConfigEntry<KeyboardShortcut> CaptureKey { get; }
        internal ConfigEntry<KeyboardShortcut> DiagnosticReportKey { get; }
        internal ConfigEntry<float> CaptureDurationSeconds { get; }
        internal ConfigEntry<bool> ExportCsv { get; }
        internal ConfigEntry<bool> MethodTimingEnabled { get; }
        internal ConfigEntry<bool> ShadowEnabled { get; }
        internal ConfigEntry<float> ShadowDistance { get; }
        internal ConfigEntry<float> ShadowUpdateInterval { get; }
        internal ConfigEntry<KeyboardShortcut> ShadowToggleKey { get; }
        internal ConfigEntry<bool> ShadowDryRun { get; }

        internal ValidatedConfiguration Validated => ConfigurationValidator.Validate(CaptureDurationSeconds.Value, ShadowDistance.Value, ShadowUpdateInterval.Value);
    }
}
