using Cute;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using Wizard;
using Wizard.Battle.Resource;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    public class CardParameterPatch
    {
        private static readonly HashSet<string> VariantIdentityFields = new HashSet<string>
        {
            nameof(CardParameter.CardId),
            nameof(CardParameter.IsFoil),
            nameof(CardParameter.CardHashId)
        };

        public bool newCard = false;
        public int cardId = 0;
        public int templateCardId;
        public Dictionary<string, bool> boolFields = [];
        public Dictionary<string, int> intFields = [];
        public Dictionary<string, string> stringChangeFields = [];
        public Dictionary<string, string> stringAppendFields = [];
        public Dictionary<string, string[]> stringArrayFields = [];
        public Dictionary<string, string> localizationFields = [];
        public AttackEffectParameterPatch attackEffectFields = new AttackEffectParameterPatch();

        public void PatchTemplate(CardParameter card, bool preserveVariantIdentity = false)
        {
            if (card == null)
            {
                Plugin.Logger.LogWarning($"Cannot patch null card for template {templateCardId}");
                return;
            }

            ApplyFields(card, boolFields, preserveVariantIdentity);
            ApplyFields(card, intFields, preserveVariantIdentity);
            ApplyFields(card, stringChangeFields, preserveVariantIdentity);

            if (stringAppendFields != null)
            {
                foreach (var kvp in stringAppendFields)
                {
                    TrySetProperty(card, kvp.Key, property =>
                    {
                        string oldValue = (string)property.GetValue(card);
                        return (oldValue ?? string.Empty) + kvp.Value;
                    }, preserveVariantIdentity);
                }
            }

            if (stringArrayFields != null)
            {
                foreach (var kvp in stringArrayFields)
                {
                    string[] value = kvp.Value == null ? null : (string[])kvp.Value.Clone();
                    TrySetProperty(card, kvp.Key, _ => value, preserveVariantIdentity);
                }
            }

            ApplyAttackEffectFields(card);

            if (localizationFields != null)
            {
                foreach (var kvp in localizationFields)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        CardMasterPatcher.CustomLocalization[$"{card.CardId}_{kvp.Key}"] = kvp.Value;
                    }
                }
            }
        }

        private void ApplyAttackEffectFields(CardParameter card)
        {
            if (attackEffectFields == null || card?.AtkEffectParameter == null)
            {
                return;
            }

            if (attackEffectFields.effectPath != null)
            {
                card.AtkEffectParameter._effectPath = ToStringPairList(attackEffectFields.effectPath);
            }

            if (attackEffectFields.se != null)
            {
                card.AtkEffectParameter._se = ToStringPairList(attackEffectFields.se);
            }

            if (attackEffectFields.moveType != null)
            {
                card.AtkEffectParameter._moveType = ToPairList(
                    attackEffectFields.moveType,
                    value => ParseEnum(value, EffectMgr.MoveType.NONE));
            }

            if (attackEffectFields.effectEnginType != null)
            {
                card.AtkEffectParameter._effectEnginType = ToPairList(
                    attackEffectFields.effectEnginType,
                    value => ParseEnum(value, EffectMgr.EngineType.NONE));
            }

            if (attackEffectFields.time != null)
            {
                card.AtkEffectParameter._time = ToPairList(attackEffectFields.time, value => value);
            }
        }

        private static List<string> ToStringPairList(IEnumerable<string> values)
        {
            List<string> list = values == null
                ? new List<string>()
                : values.Select(value => value ?? string.Empty).ToList();
            if (list.Count == 0)
            {
                return new List<string> { string.Empty, string.Empty };
            }

            if (list.Count == 1)
            {
                list.Add(list[0]);
            }
            else if (list.Count > 2)
            {
                list = list.Take(2).ToList();
            }

            return list;
        }

        private static List<TOut> ToPairList<TIn, TOut>(IEnumerable<TIn> values, Func<TIn, TOut> converter)
        {
            List<TOut> list = values == null
                ? new List<TOut>()
                : values.Select(converter).ToList();
            if (list.Count == 0)
            {
                return new List<TOut> { default(TOut), default(TOut) };
            }

            if (list.Count == 1)
            {
                list.Add(list[0]);
            }
            else if (list.Count > 2)
            {
                list = list.Take(2).ToList();
            }

            return list;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            if (int.TryParse(value, out int number))
            {
                return (T)Enum.ToObject(typeof(T), number);
            }

            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private void ApplyFields<T>(
            CardParameter card,
            Dictionary<string, T> fields,
            bool preserveVariantIdentity)
        {
            if (fields == null)
            {
                return;
            }

            foreach (var kvp in fields)
            {
                TrySetProperty(card, kvp.Key, property => ConvertValue(property, kvp.Value),
                    preserveVariantIdentity);
            }
        }

        private void TrySetProperty(
            CardParameter card,
            string propertyName,
            Func<PropertyInfo, object> valueFactory,
            bool preserveVariantIdentity)
        {
            if (preserveVariantIdentity && VariantIdentityFields.Contains(propertyName))
            {
                return;
            }

            try
            {
                PropertyInfo property = AccessTools.Property(typeof(CardParameter), propertyName);
                if (property == null || !property.CanWrite)
                {
                    Plugin.Logger.LogWarning(
                        $"CardParameter property '{propertyName}' is missing or read-only; skipping it");
                    return;
                }

                property.SetValue(card, valueFactory(property));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(
                    $"Error patching card {card.CardId} property '{propertyName}': {e.Message}");
            }
        }

        private static object ConvertValue<T>(PropertyInfo property, T value)
        {
            if (property.PropertyType.IsEnum && value is int enumValue)
            {
                return Enum.ToObject(property.PropertyType, enumValue);
            }

            return value;
        }

        public void ConvertFrom(CardParameter original)
        {
            PropertyInfo[] properties = typeof(CardParameter).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            templateCardId = original.CardId;
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanWrite||!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (property.Name == "CardId")
                {
                    continue;
                }

                object value = property.GetValue(original);
                Type propType = property.PropertyType;

                if (propType == typeof(int))
                {
                    this.intFields[property.Name] = (int)value;
                }
                else if (propType == typeof(bool))
                {
                    this.boolFields[property.Name] = (bool)value;
                }
                else if (propType == typeof(string))
                {
                    if (value != null)
                    {
                        this.stringChangeFields[property.Name] = (string)value;
                    }
                }
                else if (propType == typeof(string[]))
                {
                    if (value != null)
                    {
                        this.stringArrayFields[property.Name] = (string[])value;
                    }
                }
                else if (propType.IsEnum)
                {
                    this.intFields[property.Name] = (int)value;
                }
            }

            if (original.AtkEffectParameter != null)
            {
                this.attackEffectFields = new AttackEffectParameterPatch
                {
                    effectPath = new[]
                    {
                        original.AtkEffectParameter.GetEffectPath(false),
                        original.AtkEffectParameter.GetEffectPath(true)
                    },
                    se = new[]
                    {
                        original.AtkEffectParameter.GetSe(false),
                        original.AtkEffectParameter.GetSe(true)
                    },
                    moveType = new[]
                    {
                        original.AtkEffectParameter.GetMoveType(false).ToString(),
                        original.AtkEffectParameter.GetMoveType(true).ToString()
                    },
                    effectEnginType = new[]
                    {
                        original.AtkEffectParameter.GetEffectEnginType(false).ToString(),
                        original.AtkEffectParameter.GetEffectEnginType(true).ToString()
                    },
                    time = new[]
                    {
                        original.AtkEffectParameter.GetTime(false),
                        original.AtkEffectParameter.GetTime(true)
                    }
                };
            }
        }
    }

    /// <summary>
    /// The game's attack effect data is stored in a nested AttackEffectParameter
    /// rather than as writable CardParameter properties. Keep it as a small,
    /// human-editable pair of normal/evolved values in the patch JSON.
    /// </summary>
    public class AttackEffectParameterPatch
    {
        public string[] effectPath;
        public string[] se;
        public string[] moveType;
        public string[] effectEnginType;
        public float[] time;
    }
    public class CardMasterPatcher
    {
        public static Dictionary<int,CardParameter> CardParameterBackup = [];
        public static Dictionary<string, string> CustomLocalization = [];
        private static readonly ConditionalWeakTable<CardParameter, RuntimeCardText>
            RuntimeCardTexts = new ConditionalWeakTable<CardParameter, RuntimeCardText>();

        private sealed class RuntimeCardText
        {
            public string CardName;
            public string TribeName;
            public string SkillDescription;
            public string EvoSkillDescription;
            public string Description;
            public string EvoDescription;
        }

        public static void SetRuntimeCardText(
            CardParameter parameter,
            string cardName,
            string tribeName,
            string skillDescription,
            string evoSkillDescription,
            string description,
            string evoDescription)
        {
            if (parameter == null)
            {
                return;
            }

            RuntimeCardTexts.Remove(parameter);
            RuntimeCardTexts.Add(parameter, new RuntimeCardText
            {
                CardName = cardName,
                TribeName = tribeName,
                SkillDescription = skillDescription,
                EvoSkillDescription = evoSkillDescription,
                Description = description,
                EvoDescription = evoDescription
            });
        }


        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.CardName), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_CardName_Get(ref CardParameter __instance, ref string __result)
        {
            if (RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                __result = runtimeText.CardName;
                return false;
            }

            var id = __instance.CardId;
            var key = $"{id}_CardName";
            if (CustomLocalization.TryGetValue(key, out string result)) {
                
                __result = result;
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.TribeName), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_TribeName_Get(ref CardParameter __instance, ref string __result)
        {
            if (!RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                return true;
            }

            __result = runtimeText.TribeName;
            return false;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.SkillDescription), MethodType.Getter)]
        [HarmonyPrefix]
        public static bool CardParameter_SkillDescription_Get(ref CardParameter __instance, ref string __result)
        {
            if (RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                __result = runtimeText.SkillDescription;
                return false;
            }

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
            if (RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                __result = runtimeText.EvoSkillDescription;
                return false;
            }

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
            if (RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                __result = runtimeText.Description;
                return false;
            }

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
            if (RuntimeCardTexts.TryGetValue(__instance, out RuntimeCardText runtimeText))
            {
                __result = runtimeText.EvoDescription;
                return false;
            }

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
            Dictionary<int, CardParameter> masterDict = (Dictionary<int, CardParameter>)AccessTools.Field(typeof(CardMaster), "m_cardParameters").GetValue(master);
            var card_master_folder = Directory.CreateDirectory(Plugin.CardMasterPath);
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
                        HashSet<int> variantIds = new HashSet<int>
                        {
                            template.CardId,
                            template.NormalCardId,
                            template.FoilCardId
                        };
                        foreach (int variantId in variantIds)
                        {
                            CardParameter variant = master.GetCardParameterFromId(variantId);
                            if (variant == null)
                            {
                                Plugin.Logger.LogWarning(
                                    $"related card version {variantId} for {template.CardId} not found");
                                continue;
                            }

                            patch.PatchTemplate(variant, preserveVariantIdentity: true);
                        }
                    }
                    else
                    {
                        Plugin.Logger.LogInfo($"adding new card {patch.cardId} with tempalte: {template.CardId}");
                        if (masterDict.ContainsKey(patch.cardId))
                        {
                            Plugin.Logger.LogWarning($"card {patch.cardId} already exists, skipping");
                        }
                        else
                        {
                            var newCard = CardParameterCloner.DeepClone(template);

                            // Battle image refreshes (evolve, recovery and return-to-hand)
                            // resolve the card through BaseParameter.CardId. A cloned card
                            // must not keep the template's internal identity even though it
                            // is inserted into the master dictionary under patch.cardId.
                            // Set it before PatchTemplate so localizationFields are also
                            // registered under the new card's ID.
                            newCard.CardId = patch.cardId;
                            patch.PatchTemplate(newCard);
                            if (HasExplicitIntField(patch, nameof(CardParameter.CardId)) &&
                                newCard.CardId != patch.cardId)
                            {
                                Plugin.Logger.LogWarning(
                                    $"new card {patch.cardId} ignores intFields.CardId={newCard.CardId}; " +
                                    "the card's internal CardId must match its master key");
                            }

                            newCard.CardId = patch.cardId;
                            if (!HasExplicitIntField(patch, nameof(CardParameter.BaseCardId)))
                            {
                                newCard.BaseCardId = patch.cardId;
                            }

                            if (!HasExplicitIntField(patch, nameof(CardParameter.NormalCardId)))
                            {
                                newCard.NormalCardId = patch.cardId;
                            }

                            if (!HasExplicitIntField(patch, nameof(CardParameter.FoilCardId)))
                            {
                                newCard.FoilCardId = patch.cardId;
                            }

                            masterDict.Add(patch.cardId, newCard);
                        }
                    }
                }
            }

            Data.Load.data.UserCardList.Clear();
            var all = master.GetAllCardIds();
            for (int i = 0; i < all.Count; i++)
            {
                UserCard userCard = new UserCard();
                userCard.card_id = all[i];
                userCard.number = 99;
                Data.Load.data.UserCardList.Add(userCard);
            }
            Plugin.Logger.LogInfo("[End apply CardMaster mods]");
        }

        private static bool HasExplicitIntField(CardParameterPatch patch, string fieldName)
        {
            return patch.intFields != null && patch.intFields.ContainsKey(fieldName);
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
        public static void ResourcesManager_FindCardMaterial(
            int cardId,
            bool isEvol,
            ref Material __result)
        {
            if (__result != null)
            {
                if (commonCardMaterial == null)
                {
                    commonCardMaterial = UnityEngine.Object.Instantiate(__result);
                }
            }

            Material customMaterial = CreateExternalCardMaterial(cardId, isEvol, __result);
            if (customMaterial != null)
            {
                __result = customMaterial;
            }
        }

        [HarmonyPatch(typeof(BattleResourceMgr), nameof(BattleResourceMgr.LoadCardImageMaterial))]
        [HarmonyPrefix]
        public static bool BattleResourceMgr_LoadCardImageMaterial(
            int cardId,
            bool isEvolution,
            ref VfxBase __result)
        {
            int resourceCardId = ResolveResourceCardId(cardId);
            if (!Utils.HasExternalTexture(resourceCardId, isEvolution))
            {
                return true;
            }

            // The original loader assumes an AssetBundle material exists and dereferences
            // null for external-only cards. The postfix below supplies the material instead.
            __result = NullVfx.GetInstance();
            return false;
        }

        [HarmonyPatch(typeof(BattleResourceMgr), nameof(BattleResourceMgr.GetCardImageMaterial))]
        [HarmonyPostfix]
        public static void BattleResourceMgr_GetCardImageMaterial(
            int cardId,
            bool isEvolution,
            ref Material __result)
        {
            int resourceCardId = ResolveResourceCardId(cardId);
            Material customMaterial = CreateExternalCardMaterial(
                resourceCardId, isEvolution, __result);
            if (customMaterial != null)
            {
                __result = customMaterial;
            }
        }

        private static int ResolveResourceCardId(int cardId)
        {
            CardParameter parameter = CardMaster.GetInstanceForBattle()?.GetCardParameterFromId(cardId);
            return parameter?.ResourceCardId ?? cardId;
        }

        private static Material CreateExternalCardMaterial(
            int resourceCardId,
            bool isEvolution,
            Material originalMaterial)
        {
            Texture2D texture = Utils.GetExternalTexture(resourceCardId, isEvolution);
            if (texture == null)
            {
                return null;
            }

            Material materialTemplate = originalMaterial ?? commonCardMaterial;
            if (materialTemplate == null)
            {
                Plugin.Logger.LogWarning(
                    $"Cannot apply custom texture {resourceCardId}: no card material template is loaded");
                return null;
            }

            Material material = UnityEngine.Object.Instantiate(materialTemplate);
            material.mainTexture = texture;
            material.SetTexture("_MainTex", texture);
            Plugin.Logger.LogInfo(
                $"Custom {(isEvolution ? "evolved" : "normal")} texture for {resourceCardId} loaded");
            return material;
        }
    }


}
