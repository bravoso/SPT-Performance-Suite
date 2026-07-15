using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using TarkovPerformanceSuite.Configuration;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal readonly struct OptimizedBotCounts
    {
        internal OptimizedBotCounts(int pmc, int scav, int boss, int rogue, int blackDivision, int ruaf, int ruafRemnant, int untar)
        {
            Pmc = pmc;
            Scav = scav;
            Boss = boss;
            Rogue = rogue;
            BlackDivision = blackDivision;
            Ruaf = ruaf;
            RuafRemnant = ruafRemnant;
            Untar = untar;
        }

        internal int Pmc { get; }
        internal int Scav { get; }
        internal int Boss { get; }
        internal int Rogue { get; }
        internal int BlackDivision { get; }
        internal int Ruaf { get; }
        internal int RuafRemnant { get; }
        internal int Untar { get; }
    }

    internal sealed class OptimizedBotCounterFeature
    {
        // MoreBotsAPI logs these exact IDs at startup. Name fallbacks below keep the
        // counter correct if a future version assigns different numeric values.
        private static readonly HashSet<int> BlackDivisionRoles = new HashSet<int> { 848420, 848421, 848422, 848423 };
        private static readonly HashSet<int> RuafRoles = new HashSet<int> { 848400, 848401, 848402, 848403, 848404, 848405 };
        private static readonly HashSet<int> RuafRemnantRoles = new HashSet<int> { 848406 };
        private static readonly HashSet<int> UntarRoles = new HashSet<int> { 1170, 1171, 1172, 1173 };
        private static readonly HashSet<int> BossRoles = BuildBossRoles();
        private static readonly string[] BossNames =
        {
            "Reshala", "Killa", "Shturman", "Glukhar", "Sanitar", "Tagilla",
            "Knight", "Big Pipe", "Birdeye", "Zryachiy", "Kaban", "Kollontay",
            "Partisan", "Cultist Priest", "Oni", "Predvestnik", "Prizrak"
        };
        private static readonly Dictionary<int, int> NamedBossIndex = BuildNamedBossIndex();

        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly EntityRegistry _entities;
        private readonly string[] _lines = new string[12];
        private readonly int[] _bossNameCounts = new int[BossNames.Length];
        private readonly int[] _workingBossNameCounts = new int[BossNames.Length];
        private GUIStyle _style;
        private Font _customFont;
        private string _loadedFontName;
        private float _nextRefresh;
        private OptimizedBotCounts _counts;
        private int _lineCount;
        private bool _raidActive;
        private bool _legacyCounterDisabled;

        internal OptimizedBotCounterFeature(ManualLogSource logger, PluginConfiguration configuration, EntityRegistry entities)
        {
            _logger = logger;
            _configuration = configuration;
            _entities = entities;
            UpdateLines(default);
        }

        internal OptimizedBotCounts Counts => _counts;
        internal string StatusText => _configuration.BotCounterEnabled.Value
            ? "enabled | cached registry | living bots | named bosses | custom factions separated"
            : "disabled";

        internal void Initialize()
        {
            if (_configuration.BotCounterEnabled.Value) DisableLegacyCounterIfPresent();
        }

        internal void OnRaidStarted()
        {
            if (_configuration.BotCounterEnabled.Value) DisableLegacyCounterIfPresent();
            _raidActive = true;
            _nextRefresh = 0;
            _counts = default;
            Array.Clear(_bossNameCounts, 0, _bossNameCounts.Length);
            UpdateLines(_counts);
        }

        internal void OnRaidEnded()
        {
            _raidActive = false;
            _counts = default;
            Array.Clear(_bossNameCounts, 0, _bossNameCounts.Length);
            UpdateLines(_counts);
        }

        internal void Tick(float now)
        {
            if (!_raidActive || !_configuration.BotCounterEnabled.Value || now < _nextRefresh) return;
            DisableLegacyCounterIfPresent();
            float rate = Clamp(_configuration.BotCounterRefreshRate.Value, 0.5f, 5f);
            _nextRefresh = now + 1f / rate;
            int pmc = 0, scav = 0, boss = 0, rogue = 0, blackDivision = 0, ruaf = 0, ruafRemnant = 0, untar = 0;
            Array.Clear(_workingBossNameCounts, 0, _workingBossNameCounts.Length);

            foreach (TrackedEntity entity in _entities.Entities)
            {
                Player player = entity.Player;
                if (player == null || entity.Kind != Features.EntityKind.RemoteAI || !entity.IsAlive) continue;
                Profile profile = player.Profile;
                if (profile?.Info?.Settings == null) continue;
                int role = (int)profile.Info.Settings.Role;
                string roleName = profile.Info.Settings.Role.ToString();

                // Custom factions use boss-like brain settings and some UNTAR enum names start
                // with "boss", but their server definitions explicitly set isBoss=false.
                if (IsBlackDivision(role, roleName)) { blackDivision++; continue; }
                if (IsRuafRemnant(role, roleName)) { ruafRemnant++; continue; }
                if (IsRuaf(role, roleName)) { ruaf++; continue; }
                if (IsUntar(role, roleName)) { untar++; continue; }

                EPlayerSide side = profile.Side;
                if (side == EPlayerSide.Bear || side == EPlayerSide.Usec) { pmc++; continue; }
                if (role == (int)WildSpawnType.pmcBot || role == (int)WildSpawnType.exUsec || role == (int)WildSpawnType.arenaFighterEvent)
                {
                    rogue++;
                    continue;
                }
                if (BossRoles.Contains(role))
                {
                    boss++;
                    if (NamedBossIndex.TryGetValue(role, out int index)) _workingBossNameCounts[index]++;
                }
                else scav++;
            }

            _counts = new OptimizedBotCounts(pmc, scav, boss, rogue, blackDivision, ruaf, ruafRemnant, untar);
            Array.Copy(_workingBossNameCounts, _bossNameCounts, _bossNameCounts.Length);
            // This runs at only 0.5-5 Hz and lets F12 display choices apply immediately even
            // when no bot spawned or died during the interval.
            UpdateLines(_counts);
        }

        internal void Draw()
        {
            if (!_raidActive || !_configuration.BotCounterEnabled.Value) return;
            RefreshStyle();
            int lineHeight = _style.fontSize + 5;
            const float width = 520f;
            float x = Screen.width - width - _configuration.BotCounterOffsetRight.Value;
            float y = _configuration.BotCounterOffsetTop.Value;
            for (int i = 0; i < _lineCount; i++)
            {
                Rect rect = new Rect(x, y + i * lineHeight, width, lineHeight);
                _style.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _lines[i], _style);
                _style.normal.textColor = Color.green;
                GUI.Label(rect, _lines[i], _style);
            }
        }

        private void RefreshStyle()
        {
            if (_style == null)
            {
                _style = new GUIStyle
                {
                    alignment = TextAnchor.UpperRight
                };
            }
            _style.fontSize = Clamp(_configuration.BotCounterFontSize.Value, 10, 30);
            _style.fontStyle = _configuration.BotCounterFontStyle.Value;

            string requested = (_configuration.BotCounterFontName.Value ?? string.Empty).Trim();
            if (string.Equals(requested, _loadedFontName, StringComparison.OrdinalIgnoreCase)) return;
            _loadedFontName = requested;
            if (_customFont != null)
            {
                UnityEngine.Object.Destroy(_customFont);
                _customFont = null;
            }
            _style.font = null;
            if (requested.Length == 0) return;
            try
            {
                _customFont = Font.CreateDynamicFontFromOSFont(requested, _style.fontSize);
                _style.font = _customFont;
                if (_customFont == null) _logger.LogWarning("Bot counter font '" + requested + "' was not found; using EFT's default font.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Bot counter font '" + requested + "' could not be loaded; using EFT's default font: " + ex.Message);
            }
        }

        private void DisableLegacyCounterIfPresent()
        {
            if (_legacyCounterDisabled) return;
            foreach (KeyValuePair<string, BepInEx.PluginInfo> pair in Chainloader.PluginInfos)
            {
                BepInEx.PluginInfo info = pair.Value;
                if (!string.Equals(info?.Metadata?.GUID, "com.yourname.botcounter", StringComparison.OrdinalIgnoreCase)) continue;
                if (info.Instance is BaseUnityPlugin plugin)
                {
                    ConfigDefinition definition = new ConfigDefinition("1. General", "Enable Mod");
                    if (plugin.Config.TryGetEntry(definition, out ConfigEntry<bool> enabled)) enabled.Value = false;
                }
                _legacyCounterDisabled = true;
                _logger.LogWarning("Disabled the legacy SPT Detailed Bot Counter because the suite now provides a cached replacement without periodic scans.");
                return;
            }
        }

        private void UpdateLines(OptimizedBotCounts counts)
        {
            _lineCount = 0;
            bool hideZero = _configuration.BotCounterHideZeroCategories.Value;
            AddCountLine("AI PMCs", counts.Pmc, hideZero);
            AddCountLine("Scavs", counts.Scav, hideZero);

            int namedBosses = 0;
            for (int i = 0; i < _bossNameCounts.Length; i++) namedBosses += _bossNameCounts[i];
            if (_configuration.BotCounterShowBossNames.Value)
            {
                if (namedBosses > 0) AddLine(BuildNamedBossLine());
                else if (!hideZero) AddLine("Bosses: none");
                AddCountLine("Boss guards & followers", Math.Max(0, counts.Boss - namedBosses), hideZero);
            }
            else AddCountLine("Bosses & Guards", counts.Boss, hideZero);

            AddCountLine("Rogues & Raiders", counts.Rogue, hideZero);
            AddCountLine("Black Division", counts.BlackDivision, hideZero);
            AddCountLine("RUAF", counts.Ruaf, hideZero);
            AddCountLine("RUAF Remnants", counts.RuafRemnant, hideZero);
            AddCountLine("UNTAR", counts.Untar, hideZero);
        }

        private string BuildNamedBossLine()
        {
            var text = new StringBuilder(96);
            text.Append("Bosses: ");
            bool first = true;
            for (int i = 0; i < _bossNameCounts.Length; i++)
            {
                int count = _bossNameCounts[i];
                if (count <= 0) continue;
                if (!first) text.Append(", ");
                text.Append(BossNames[i]);
                if (count > 1) text.Append(" x").Append(count);
                first = false;
            }
            return text.ToString();
        }

        private void AddCountLine(string label, int count, bool hideZero)
        {
            if (!hideZero || count > 0) AddLine(label + ": " + count);
        }

        private void AddLine(string text)
        {
            if (_lineCount < _lines.Length) _lines[_lineCount++] = text;
        }

        private static bool IsBlackDivision(int role, string name)
            => BlackDivisionRoles.Contains(role) || StartsWith(name, "blackDiv");

        private static bool IsRuafRemnant(int role, string name)
            => RuafRemnantRoles.Contains(role) || StartsWith(name, "remnant");

        private static bool IsRuaf(int role, string name)
            => RuafRoles.Contains(role) || StartsWith(name, "ruaf");

        private static bool IsUntar(int role, string name)
            => UntarRoles.Contains(role) || name.IndexOf("untar", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool StartsWith(string value, string prefix)
            => value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static HashSet<int> BuildBossRoles()
        {
            var result = new HashSet<int> { (int)WildSpawnType.crazyAssaultEvent, (int)WildSpawnType.infectedTagilla };
            Array values = Enum.GetValues(typeof(WildSpawnType));
            for (int i = 0; i < values.Length; i++)
            {
                var role = (WildSpawnType)values.GetValue(i);
                string name = role.ToString();
                if (name.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("follower", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("sectant", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add((int)role);
            }
            return result;
        }

        private static Dictionary<int, int> BuildNamedBossIndex()
        {
            var result = new Dictionary<int, int>();
            AddBossRole(result, WildSpawnType.bossBully, 0);
            AddBossRole(result, WildSpawnType.bossKilla, 1);
            AddBossRole(result, WildSpawnType.bossKillaAgro, 1);
            AddBossRole(result, WildSpawnType.bossKojaniy, 2);
            AddBossRole(result, WildSpawnType.bossGluhar, 3);
            AddBossRole(result, WildSpawnType.bossSanitar, 4);
            AddBossRole(result, WildSpawnType.bossTagilla, 5);
            AddBossRole(result, WildSpawnType.bossTagillaAgro, 5);
            AddBossRole(result, WildSpawnType.infectedTagilla, 5);
            AddBossRole(result, WildSpawnType.bossKnight, 6);
            AddBossRole(result, WildSpawnType.followerBigPipe, 7);
            AddBossRole(result, WildSpawnType.followerBirdEye, 8);
            AddBossRole(result, WildSpawnType.bossZryachiy, 9);
            AddBossRole(result, WildSpawnType.bossBoar, 10);
            AddBossRole(result, WildSpawnType.bossKolontay, 11);
            AddBossRole(result, WildSpawnType.bossPartisan, 12);
            AddBossRole(result, WildSpawnType.sectantPriest, 13);
            AddBossRole(result, WildSpawnType.sectantOni, 14);
            AddBossRole(result, WildSpawnType.sectantPredvestnik, 15);
            AddBossRole(result, WildSpawnType.sectantPrizrak, 16);
            return result;
        }

        private static void AddBossRole(Dictionary<int, int> roles, WildSpawnType role, int index)
            => roles[(int)role] = index;

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static float Clamp(float value, float minimum, float maximum)
            => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
