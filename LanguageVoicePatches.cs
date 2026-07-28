using Cute;
using HarmonyLib;

namespace Shadowbus
{
    public static class LanguageVoicePatches
    {
        private static bool preserveVoiceCacheDuringLanguageReset;

        [HarmonyPatch(typeof(SoftwareResetScene), "Start")]
        [HarmonyPrefix]
        public static void SoftwareResetScene_Start_Prefix()
        {
            preserveVoiceCacheDuringLanguageReset =
                Toolbox.AssetManager != null &&
                !string.IsNullOrEmpty(
                    Toolbox.SavedataManager.GetString(SavedataManager.LANGUAGE_CHANGE, string.Empty));
        }

        [HarmonyPatch(typeof(SoftwareResetScene), "Start")]
        [HarmonyPostfix]
        public static void SoftwareResetScene_Start_Postfix()
        {
            preserveVoiceCacheDuringLanguageReset = false;
        }

        [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.ClearSoundFileVoice))]
        [HarmonyPrefix]
        public static bool AssetManager_ClearSoundFileVoice_Prefix()
        {
            if (!preserveVoiceCacheDuringLanguageReset)
            {
                return true;
            }

            preserveVoiceCacheDuringLanguageReset = false;
            Plugin.Logger.LogInfo("[LanguageVoice] Preserved the voice cache during language reset.");
            return false;
        }
    }
}
