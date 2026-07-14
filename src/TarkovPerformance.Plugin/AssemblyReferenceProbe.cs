using BepInEx;
using EFT;
using HarmonyLib;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarkovPerformanceSuite
{
    internal static class AssemblyReferenceProbe
    {
        internal static readonly System.Type[] VerifiedTypes =
        {
            typeof(BaseUnityPlugin),
            typeof(Harmony),
            typeof(Player),
            typeof(GameWorld),
            typeof(ProfilerRecorder),
            typeof(Renderer),
            typeof(ShadowCastingMode)
        };
    }
}

