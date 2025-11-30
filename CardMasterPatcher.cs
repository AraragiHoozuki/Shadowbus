using Cute;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Wizard;

namespace Shadowbus
{
    public class CardParameterPatch
    {
        public bool newCard;
        public int cardId;
        public int templateCardId;
        public Dictionary<string, bool> boolFields;
        public Dictionary<string, int> intFields;
        public Dictionary<string, string> stringChangeFields;
        public Dictionary<string, string> stringAppendFields;
        public Dictionary<string, string[]> stringArrayFields;
        public Dictionary<string, string> localizationFields;

        public void PatchTemplate(CardParameter card)
        {
            try
            {
                if (boolFields != null)
                {
                    foreach (var kvp in boolFields)
                    {
                        AccessTools.Property(typeof(CardParameter), kvp.Key).SetValue(card, kvp.Value);
                    }
                }
                if (intFields != null)
                {
                    foreach (var kvp in intFields)
                    {
                        AccessTools.Property(typeof(CardParameter), kvp.Key).SetValue(card, kvp.Value);
                    }
                }
                if (stringChangeFields != null)
                {
                    foreach (var kvp in stringChangeFields)
                    {
                        AccessTools.Property(typeof(CardParameter), kvp.Key).SetValue(card, kvp.Value);
                    }
                }
                if (stringAppendFields != null)
                {
                    foreach (var kvp in stringAppendFields)
                    {
                        var old = (string)AccessTools.Property(typeof(CardParameter), kvp.Key).GetValue(card);
                        AccessTools.Property(typeof(CardParameter), kvp.Key).SetValue(card, old + kvp.Value);
                    }
                }
                if (localizationFields != null)
                {
                    foreach (var kvp in localizationFields)
                    {
                        if (!string.IsNullOrEmpty(kvp.Value))
                        {
                            CardMasterPatcher.CustomLocalization.Add($"{card.CardId}_{kvp.Key}", kvp.Value);
                        }
                        
                    }
                }

            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Error patching card {cardId}: {e.Message}");
            }

        }
    }
    public class CardMasterPatcher
    {
        public static Dictionary<int,CardParameter> CardParameterBackup = [];
        public static Dictionary<string, string> CustomLocalization = [];


        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.CardName), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_CardName_Get(ref CardParameter __instance, ref string __result)
        {
            var id = __instance.CardId;
            var key = $"{id}_CardName";
            if (CustomLocalization.TryGetValue(key, out string result)) {
                
                __result = result;
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.SkillDescription), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_SkillDescription_Get(ref CardParameter __instance, ref string __result)
        {
            var id = __instance.CardId;
            var key = $"{id}_SkillDescription";
            if (CustomLocalization.TryGetValue(key, out string result))
            {

                __result = result;
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.EvoSkillDescription), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_EvoSkillDescription_Get(ref CardParameter __instance, ref string __result)
        {
            var id = __instance.CardId;
            var key = $"{id}_EvoSkillDescription";
            if (CustomLocalization.TryGetValue(key, out string result))
            {

                __result = result;
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.Description), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_Description_Get(ref CardParameter __instance, ref string __result)
        {
            var id = __instance.CardId;
            var key = $"{id}_Description";
            if (CustomLocalization.TryGetValue(key, out string result))
            {

                __result = result;
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.EvoDescription), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_EvoDescription_Get(ref CardParameter __instance, ref string __result)
        {
            var id = __instance.CardId;
            var key = $"{id}_EvoDescription";
            if (CustomLocalization.TryGetValue(key, out string result))
            {

                __result = result;
                return false;
            }
            return true;
        }


        public static void BackupCardMaster(CardMaster master)
        {
            Plugin.Logger.LogInfo("Backup Current CardMaster");
            IDictionary<int, CardParameter> masterDict = (IDictionary<int, CardParameter>)AccessTools.Field(typeof(CardMaster), "m_cardParameters").GetValue(master);
            CardParameterBackup.Clear();
            foreach (var kvp in masterDict)
            {
                CardParameterBackup.Add(kvp.Key, kvp.Value.Clone());
            }
        }
        public static void RevokeCardMasterPatches(CardMaster master = null)
        {
            Plugin.Logger.LogInfo("Revoke CardMaster mods");
            master ??= CardMaster.GetInstanceForBattle();
            IDictionary<int, CardParameter> masterDict = (IDictionary<int, CardParameter>)AccessTools.Field(typeof(CardMaster), "m_cardParameters").GetValue(master);
            masterDict.Clear();
            CustomLocalization.Clear();
            foreach (var kvp in CardParameterBackup)
            {
                masterDict.Add(kvp.Key,kvp.Value.Clone());
            }
        }
        public static void ApplyCardMasterPatches(CardMaster master = null)
        {
            Plugin.Logger.LogInfo("[Begin apply CardMaster mods]");
            master ??= CardMaster.GetInstanceForBattle();
            RevokeCardMasterPatches(master);
            var mods_folder = Directory.CreateDirectory("Mods");
            var card_master_folder = mods_folder.CreateSubdirectory("CardMaster");
            var patches = card_master_folder.GetFiles("*.json");
            foreach (var pat in patches)
            {
                string json = File.ReadAllText(pat.FullName);
                List<CardParameterPatch> card_patches = JsonConvert.DeserializeObject<List<CardParameterPatch>>(json);
                foreach (var patch in card_patches)
                {
                    var template = master.GetCardParameterFromId(patch.templateCardId);
                    if (template == null)
                    {
                        Plugin.Logger.LogWarning($"template card {patch.templateCardId} not found");
                    }
                    else if (!patch.newCard)
                    {
                        Plugin.Logger.LogInfo($"patching card {template.CardId}");
                        if (template.IsFoil)
                        {
                            patch.PatchTemplate(master.GetCardParameterFromId(template.BaseCardId));
                        }
                        else
                        {
                            patch.PatchTemplate(master.GetCardParameterFromId(template.FoilCardId));
                        }
                        patch.PatchTemplate(template);
                    }
                    else
                    {

                    }
                }
            }
            Plugin.Logger.LogInfo("[End apply CardMaster mods]");
        }

        [HarmonyPatch(typeof(Wizard.CardMaster), "CreateCardMaster")]
        [HarmonyPostfix]
        public static void CardMaster_CreateCardMaster_post(ref CardMaster __result)
        {
            
            BackupCardMaster(__result);
            ApplyCardMasterPatches(__result);
        }


        public static Material commonCardMaterial;
        public static Material foilcardMaterial;

        [HarmonyPatch(typeof(Cute.ResourcesManager), nameof(Cute.ResourcesManager.FindCardMaterial))]
        [HarmonyPostfix]
        public static void ResourcesManager_FindCardMaterial(ref int cardId, ref Material __result)
        {
            if (__result != null)
            {
                if (commonCardMaterial == null)
                {
                    commonCardMaterial = UnityEngine.Object.Instantiate(__result);
                }
            }

            if (__result == null) {
                Plugin.Logger.LogInfo($"Custom texture for {cardId} loaded");
                Material newMat = UnityEngine.Object.Instantiate(commonCardMaterial);
                Texture2D custom_texture = Utils.GetExternalTexture(cardId);
                newMat.mainTexture = custom_texture;
                newMat.SetTexture("_MainTex", custom_texture);
                __result = newMat;
            }
        }
    }


}
