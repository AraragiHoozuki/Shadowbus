using Cute;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using Wizard;

namespace Shadowbus
{
    internal static class LocalDeckCodeService
    {
        private sealed class ExportMetadata
        {
            internal string FormatId;
            internal string DeckName;
            internal long SleeveId;
            internal int SkinId;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<GenerateDeckCodeTask, ExportMetadata> ExportMetadataByTask =
            new Dictionary<GenerateDeckCodeTask, ExportMetadata>();
        private static LocalDeckCodePayload importedPayload;

        internal static bool CanHandleTaskName(string taskName)
        {
            return taskName == nameof(GenerateDeckCodeTask) ||
                taskName == nameof(GetDeckDataFromCodeTask);
        }

        internal static bool CanHandle(NetworkTask task)
        {
            return task is GenerateDeckCodeTask || task is GetDeckDataFromCodeTask;
        }

        internal static void RegisterExport(GenerateDeckCodeTask task, DeckData deck)
        {
            if (task == null || deck == null)
            {
                return;
            }

            var metadata = new ExportMetadata
            {
                FormatId = CustomDeckStore.GetDeckFormatId(deck.GetDeckID()),
                DeckName = deck.GetDeckName(),
                SleeveId = deck.GetDeckSleeveID(),
                SkinId = deck.GetRawSkinId()
            };
            lock (Sync)
            {
                ExportMetadataByTask[task] = metadata;
            }
        }

        internal static bool TryCreateResponse(NetworkTask task, out JsonData response)
        {
            if (task is GenerateDeckCodeTask generateTask)
            {
                LocalDeckCodePayload payload = CreatePayload(generateTask);
                string code = LocalDeckCode.Encode(payload);
                response = CreateResponse(new
                {
                    deck_code = code
                });
                Plugin.Logger.LogInfo(
                    $"[DeckCode] Generated a local code for {payload.CardIds.Count} card(s), " +
                    $"format={payload.FormatId ?? CustomFormats.UnlimitedId}, length={code.Length}.");
                return true;
            }

            if (task is GetDeckDataFromCodeTask importTask)
            {
                var parameters = importTask.Params as
                    GetDeckDataFromCodeTask.GetDeckDataFromCodeTaskParam;
                string error = null;
                LocalDeckCodePayload payload = null;
                if (parameters == null || !LocalDeckCode.TryDecode(
                    parameters.deck_code,
                    out payload,
                    out error))
                {
                    throw new InvalidDataException(error ?? "The deck code is invalid.");
                }

                lock (Sync)
                {
                    importedPayload = payload;
                }
                response = CreateResponse(new
                {
                    deck = new
                    {
                        clan = payload.ClanId,
                        sub_clan = payload.SubClanId ?? 10,
                        rotation_id = payload.MyRotationId,
                        cardID = payload.CardIds.ToArray()
                    }
                });
                Plugin.Logger.LogInfo(
                    $"[DeckCode] Decoded a local code containing {payload.CardIds.Count} card(s), " +
                    $"format={payload.FormatId ?? CustomFormats.UnlimitedId}.");
                return true;
            }

            response = null;
            return false;
        }

        internal static bool TryTakeImportedPayload(
            int clanId,
            IEnumerable<int> cardIds,
            out LocalDeckCodePayload payload)
        {
            List<int> cards = cardIds?.ToList();
            lock (Sync)
            {
                payload = importedPayload;
                if (payload == null || payload.ClanId != clanId || cards == null ||
                    !payload.CardIds.SequenceEqual(cards))
                {
                    return false;
                }
                importedPayload = null;
                return true;
            }
        }

        internal static bool TryGetImportedPayload(out LocalDeckCodePayload payload)
        {
            lock (Sync)
            {
                payload = importedPayload;
                return payload != null;
            }
        }

        private static LocalDeckCodePayload CreatePayload(GenerateDeckCodeTask task)
        {
            int clanId;
            int? subClanId = null;
            int[] cardIds;
            string myRotationId = null;
            if (task.Params is GenerateDeckCodeTask.GenerateDeckCodeTaskUseSubClassParam subclass)
            {
                clanId = subclass.clan;
                subClanId = subclass.sub_clan;
                cardIds = subclass.cardID;
            }
            else if (task.Params is GenerateDeckCodeTask.GenerateDeckCodeTaskMyRotation rotation)
            {
                clanId = rotation.clan;
                cardIds = rotation.cardID;
                myRotationId = rotation.rotation_id;
            }
            else if (task.Params is GenerateDeckCodeTask.GenerateDeckCodeTaskParam normal)
            {
                clanId = normal.clan;
                cardIds = normal.cardID;
            }
            else
            {
                throw new InvalidDataException(
                    "The deck-code task did not contain supported deck parameters.");
            }

            ExportMetadata metadata = null;
            lock (Sync)
            {
                if (ExportMetadataByTask.TryGetValue(task, out metadata))
                {
                    ExportMetadataByTask.Remove(task);
                }
            }

            return new LocalDeckCodePayload
            {
                ClanId = clanId,
                SubClanId = subClanId,
                FormatId = metadata?.FormatId ?? CustomFormats.UnlimitedId,
                DeckName = metadata?.DeckName,
                SleeveId = metadata?.SleeveId,
                SkinId = metadata?.SkinId,
                MyRotationId = myRotationId,
                CardIds = cardIds?.ToList() ?? new List<int>()
            };
        }

        private static JsonData CreateResponse(object data)
        {
            string json = JsonConvert.SerializeObject(new
            {
                data_headers = new
                {
                    short_udid = 0,
                    viewer_id = 0,
                    sid = string.Empty,
                    servertime = 0L,
                    result_code = 1
                },
                data
            });
            return JsonMapper.ToObject(json);
        }
    }

    internal static class LocalDeckCodePatches
    {
        [HarmonyPatch(typeof(GetDeckDataFromCodeTask), "Parse")]
        [HarmonyPrefix]
        private static bool GetDeckDataFromCodeTask_Parse_Prefix(ref int __result)
        {
            if (!LocalDeckCodeService.TryGetImportedPayload(
                out LocalDeckCodePayload payload))
            {
                return true;
            }

            Data.DeckDataFromDeckCode = new DeckBuilder.GetDeckDataFromCode
            {
                ClanId = payload.ClanId,
                SubClanId = payload.SubClanId ?? 10,
                IsSubClanSet = payload.SubClanId.HasValue &&
                    CardBasePrm.ClanTypeIsUseable(
                        (CardBasePrm.ClanType)payload.SubClanId.Value),
                CardIds = payload.CardIds.ToArray(),
                MyRotationId = payload.MyRotationId
            };
            __result = 1;
            return false;
        }

        [HarmonyPatch(typeof(DeckDetailDialog), "SetGenerateDeckCodeTask")]
        [HarmonyPostfix]
        private static void DeckDetailDialog_SetGenerateDeckCodeTask_Postfix(
            DeckDetailDialog __instance,
            GenerateDeckCodeTask task)
        {
            LocalDeckCodeService.RegisterExport(task, __instance?._deck);
        }

        [HarmonyPatch(typeof(DeckCreateMenuUI), "OnClickDeckCode")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DeckCreateMenuUI_OnClickDeckCode_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (replaced < 2 && LoadsInteger(instruction, 16))
                {
                    instruction.opcode = OpCodes.Ldc_I4;
                    instruction.operand = LocalDeckCode.MaximumLength;
                    replaced++;
                }
                yield return instruction;
            }

            if (replaced != 2)
            {
                Plugin.Logger.LogWarning(
                    $"[DeckCode] Expected to expand two input limits, but changed {replaced}.");
            }
        }

        [HarmonyPatch(typeof(DeckCreateMenuUI), "CreateDeckFromCopyCode")]
        [HarmonyPostfix]
        private static void DeckCreateMenuUI_CreateDeckFromCopyCode_Postfix(
            int clanId,
            int[] cardIds,
            ref DeckData __result)
        {
            if (__result == null || !LocalDeckCodeService.TryTakeImportedPayload(
                clanId,
                cardIds,
                out LocalDeckCodePayload payload))
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.DeckName))
            {
                __result.SetDeckName(payload.DeckName);
            }
            if (payload.SleeveId.HasValue)
            {
                __result.SetDeckSleeveID(payload.SleeveId.Value);
            }
            if (payload.SkinId.HasValue)
            {
                __result.SetSkinId(payload.SkinId.Value);
            }
            if (!string.IsNullOrEmpty(payload.FormatId) &&
                CustomFormats.TryGet(payload.FormatId, out CustomFormatDefinition definition))
            {
                CustomFormatContext.DeckEditFormatId = definition.Id;
            }
        }

        private static bool LoadsInteger(CodeInstruction instruction, int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int integer)
            {
                return integer == value;
            }
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                return Convert.ToInt32(instruction.operand) == value;
            }
            return value >= -1 && value <= 8 &&
                instruction.opcode == new[]
                {
                    OpCodes.Ldc_I4_M1,
                    OpCodes.Ldc_I4_0,
                    OpCodes.Ldc_I4_1,
                    OpCodes.Ldc_I4_2,
                    OpCodes.Ldc_I4_3,
                    OpCodes.Ldc_I4_4,
                    OpCodes.Ldc_I4_5,
                    OpCodes.Ldc_I4_6,
                    OpCodes.Ldc_I4_7,
                    OpCodes.Ldc_I4_8
                }[value + 1];
        }
    }
}
