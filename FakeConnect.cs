using Cute;
using HarmonyLib;
using LitJson;
using MessagePack;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Wizard;
using Wizard.Bingo;
using Wizard.Scripts.Network.Data.TaskData.BuildDeckPurchase;
using Wizard.Scripts.Network.Data.TaskData.ItemPurchase;
using Wizard.Scripts.Network.Data.TaskData.SkinPurchase;
using Wizard.Scripts.Network.Data.TaskData.SpotCardExchange;

namespace Shadowbus
{
    public class FakeConnect
    {
        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Connect), [typeof(bool)])]
        [HarmonyPrefix]
        public static bool NetworkManager_Connect_Prefix(ref IEnumerator __result, NetworkManager __instance, bool showErrorDialog)
        {
            __result = CustomConnectCoroutine(__instance, showErrorDialog);
            return false;
        }

        private static IEnumerator CustomConnectCoroutine(NetworkManager __instance, bool showErrorDialog)
        {
            
            NetworkTask currentTask = __instance.lastRequestTask;
            string taskTypeName = currentTask.GetType().Name;
            if (IsTaskSkipped(taskTypeName))
            {
                Plugin.Logger.LogInfo($"[Offlinizer] 拦截并直接跳过任务: {taskTypeName}");
                yield return ProcessSkipTask(__instance, currentTask);
            } else
            if (IsTaskOfflinized(taskTypeName))
            {
                Plugin.Logger.LogInfo($"[Offlinizer] Intercepted Task: {taskTypeName}. Reading local data...");
                yield return ProcessOfflineTask(__instance, currentTask, taskTypeName);
            }
            else
            {
                Plugin.Logger.LogInfo($"[Offlinizer] Task {taskTypeName} not offlinized yet. Sending real request...");
                yield return ProcessOnlineTask(__instance, currentTask, showErrorDialog);
            }
        }
        private static string[] SkippedTaskes = new string[]
            {
            "MyPageRefreshTask",
            };
        private static bool IsTaskSkipped(string taskName)
        {
            return Array.Exists(SkippedTaskes, t => t == taskName);
        }
        private static bool IsTaskOfflinized(string taskName)
        {
            return File.Exists((Path.Combine("Mods", "OfflinizedTasks", $"{taskName}.json")));
        }
        private static IEnumerator ProcessSkipTask(NetworkManager __instance, NetworkTask task)
        {
            yield return null;
            try
            {
                task.SetResponseData(LitJson.JsonMapper.ToObject("{}"));

                task.CheckResultCodeToPopupCreate_ReturnStatus(0);

                if (task.CallbackOnUnityWebRequestDone != null)
                {
                    task.CallbackOnUnityWebRequestDone(null);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Offlinizer] 跳过任务时出现警告 (通常可忽略) {task.GetType().Name}: {ex.Message}");
            }
            if (__instance.NetworkUI != null)
            {
                __instance.NetworkUI.StopLoading();
            }
            __instance.ClearLastRequestTask();
            __instance.isConnect = false;
        }

        private static IEnumerator ProcessOfflineTask(NetworkManager __instance, NetworkTask task, string taskName)
        {
            
            while (__instance.isConnect)
            {
                yield return 0;
            }
            yield return new WaitForSeconds(0.01f); // Optional: slight delay to simulate network latency

            __instance.isConnect = true;
            __instance.isTimeOut = false;
            __instance.isError = false;

            try
            {
                string filePath = Path.Combine("Mods", "OfflinizedTasks", $"{taskName}.json");

                if (File.Exists(filePath))
                {
                    string jsonText = File.ReadAllText(filePath);
                    var data = JsonMapper.ToObject(jsonText);
                    data["data_headers"]["servertime"] = (long)TimeNativePlugin.GetDeviceOperatingTime();
                    task.SetResponseData(data);
                    if (task is CheckSpecialTitleTask specialTitleTask)
                    {
                        specialTitleTask.ParseTitleCheckData();
                    }
                    else
                    {
                        task.CheckResultCodeToPopupCreate_ReturnStatus(0);
                    }

                    Plugin.Logger.LogInfo($"[Offlinizer] Successfully injected local data for {taskName}");
                }
                else
                {
                    Plugin.Logger.LogError($"[Offlinizer] Local data file not found: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Offlinizer] Error processing local data for {taskName}: {ex.Message}");
            }

            __instance.ClearLastRequestTask();
            __instance.isConnect = false;
        }

        private static IEnumerator ProcessOnlineTask(NetworkManager __instance, NetworkTask task, bool showErrorDialog)
        {
            while (__instance.isConnect)
            {
                yield return 0;
            }
            __instance.isConnect = true;
            __instance.isTimeOut = false;
            __instance.isError = false;
            if (__instance.NetworkUI != null && __instance._showLoadingIcon)
            {
                __instance.NetworkUI.StartLoading(false);
            }
            bool isLogTraceCheckUri = false;
            if (__instance.lastRequestTask is DoMatchingBase || __instance.lastRequestTask is FinishTaskBase)
            {
                isLogTraceCheckUri = true;
            }
            string url = __instance.lastRequestTask.Url;
            NetworkTask networkTask = __instance.lastRequestTask;
            if (isLogTraceCheckUri)
            {
                __instance.LogTraceCheck("1");
            }
            using (UnityWebRequest unityWebRequest = __instance.GetUnityWebRequestInstance(url))
            {
                yield return unityWebRequest.SendWebRequest();
                if (isLogTraceCheckUri)
                {
                    __instance.LogTraceCheck("2");
                }
                float endTime = Time.realtimeSinceStartup + 30f;
                if (__instance.lastRequestTask.GetType().Equals(typeof(CheckSpecialTitleTask)))
                {
                    endTime = Time.realtimeSinceStartup + 2f;
                }
                while (!unityWebRequest.isDone && Time.realtimeSinceStartup < endTime)
                {
                    yield return 0;
                }
                if (isLogTraceCheckUri)
                {
                    __instance.LogTraceCheck("3");
                }
                if (__instance.NetworkUI != null)
                {
                    __instance.NetworkUI.StopLoading();
                }
                if (!unityWebRequest.isDone)
                {
                    __instance.isTimeOut = true;
                    LocalLog.AccumulateTraceLog("Connect is TimeOut");
                    __instance.disposeUnityWebRequest(unityWebRequest);
                    if (!__instance.lastRequestTask.isSkipCommonTimeOutPopUp())
                    {
                        if (__instance.lastRequestTask.GetType().Equals(typeof(PackOpenTask)) || __instance.lastRequestTask.GetType().Equals(typeof(BuildDeckBuyTask)) || __instance.lastRequestTask.GetType().Equals(typeof(SleeveBuyTask)) || __instance.lastRequestTask.GetType().Equals(typeof(SkinBuyMultiRewardTask)) || __instance.lastRequestTask.GetType().Equals(typeof(SkinBuyMultiTask)) || __instance.lastRequestTask.GetType().Equals(typeof(SkinBuySingleTask)) || __instance.lastRequestTask.GetType().Equals(typeof(ItemPurchaseBuyTask)) || __instance.lastRequestTask.GetType().Equals(typeof(SpotCardExchangeTask)) || __instance.lastRequestTask.GetType().Equals(typeof(CardCreateTask)) || __instance.lastRequestTask.GetType().Equals(typeof(CardDestructTask)) || __instance.lastRequestTask.GetType().Equals(typeof(StoryFinishTask)) || __instance.lastRequestTask.GetType().Equals(typeof(PracticeFinishTask)) || __instance.lastRequestTask.GetType().Equals(typeof(BingoDrawTask)) || __instance.lastRequestTask.GetType().Equals(typeof(MypageTreasureBoxCpOpenTask)) || __instance.lastRequestTask.GetType().Equals(typeof(MypageReceiveSpecialTreasureTask)) || __instance.lastRequestTask.GetType().Equals(typeof(FreeCardPackCampaignFinishTask)))
                        {
                            __instance.NetworkUI.OpenGoToTitleErrorPopUp(Data.SystemText.Get("ErrorHeader_0012"), Data.SystemText.Get("Error_0012"), "");
                        }
                        else
                        {
                            __instance.NetworkUI.OpenTimeOutErrorPopUp();
                        }
                    }
                    if (__instance.lastRequestTask.CallbackOnFailure != null)
                    {
                        if (__instance.lastRequestTask.GetType().Equals(typeof(PaymentPCFinishTask)))
                        {
                            __instance.NetworkUI.OpenGoToTitleErrorPopUp(Data.SystemText.Get("ErrorHeader_0012"), Data.SystemText.Get("Error_0012"), "");
                        }
                        else
                        {
                            __instance.lastRequestTask.CallbackOnFailure(NetworkTask.ResultCode.TimeOut);
                        }
                    }
                    Toolbox.DeviceManager.ClearIpAddress();
                }
                else if (!string.IsNullOrEmpty(unityWebRequest.error))
                {
                    LocalLog.AccumulateTraceLog("Connect is Error!" + unityWebRequest.error + " responseCode:" + unityWebRequest.responseCode.ToString());
                    __instance.isError = true;
                    if (showErrorDialog && !__instance.lastRequestTask.isSkipCommonHttpStatusErrorPopUp())
                    {
                        if (__instance.lastRequestTask.GetType().Equals(typeof(PackOpenTask)) || __instance.lastRequestTask.GetType().Equals(typeof(PaymentPCFinishTask)))
                        {
                            __instance.NetworkUI.OpenGoToTitleErrorPopUp(Data.SystemText.Get("ErrorHeader_0012"), Data.SystemText.Get("Error_0012"), "");
                        }
                        else
                        {
                            __instance.NetworkUI.OpenHttpStatusErrorPopUp();
                        }
                    }
                    __instance.disposeUnityWebRequest(unityWebRequest);
                    if (__instance.lastRequestTask.CallbackOnFailure != null)
                    {
                        __instance.lastRequestTask.CallbackOnFailure(NetworkTask.ResultCode.Error);
                    }
                    Toolbox.DeviceManager.ClearIpAddress();
                }
                else if (unityWebRequest.isDone)
                {
                    if (__instance.lastRequestTask.CallbackOnUnityWebRequestDone != null)
                    {
                        __instance.lastRequestTask.CallbackOnUnityWebRequestDone(unityWebRequest);
                    }
                    else
                    {
                        if (unityWebRequest.downloadHandler.text != null && unityWebRequest.downloadHandler.text != "")
                        {
                            try
                            {
                                byte[] array;
                                if (__instance.isEncrypt)
                                {
                                    array = CryptAES.decrypt(unityWebRequest.downloadHandler.text);
                                }
                                else
                                {
                                    array = Convert.FromBase64String(unityWebRequest.downloadHandler.text);
                                }
                                string text;
                                if (!__instance.isUseJson)
                                {
                                    text = MessagePackSerializer.ToJson(array);
                                }
                                else
                                {
                                    text = MessagePackSerializer.ToJson(array);
                                }
                                __instance.lastRequestTask.SetResponseData(JsonMapper.ToObject(text));

                                // Offlinizer: Save the response data to a local file for future offline use
                                string taskTypeName = task.GetType().Name;
                                File.WriteAllText((Path.Combine("Mods", "OfflinizedTasks", $"{taskTypeName}.json")), text);
                            }
                            catch (Exception ex)
                            {
                                string text2 = unityWebRequest.downloadHandler.text;
                                __instance.disposeUnityWebRequest(unityWebRequest);
                                if (!__instance.lastRequestTask.GetType().Equals(typeof(CheckSpecialTitleTask)))
                                {
                                    if (!__instance.isEncrypt)
                                    {
                                        LocalLog.AccumulateTraceLog(ex.ToString());
                                        throw ex;
                                    }
                                    global::Debug.LogError(text2, null);
                                    global::Debug.LogError(ex.Message, null);
                                    global::Debug.LogError(ex.StackTrace, null);
                                    if (text2.Contains("php"))
                                    {
                                        if (text2.Length > 1800)
                                        {
                                            throw new Exception(text2.Substring(1, 1800));
                                        }
                                        throw new Exception(text2);
                                    }
                                    else
                                    {
                                        __instance.HandleDeserializeException(ex);
                                    }
                                }
                            }
                            try
                            {
                                if (__instance.lastRequestTask != null)
                                {
                                    if (__instance.lastRequestTask.GetType().Equals(typeof(CheckSpecialTitleTask)))
                                    {
                                        ((CheckSpecialTitleTask)__instance.lastRequestTask).ParseTitleCheckData();
                                    }
                                    else
                                    {
                                        NetworkTask.ERROR_CODE_STATUS error_CODE_STATUS = __instance.lastRequestTask.CheckResultCodeToPopupCreate_ReturnStatus(0);
                                        if (error_CODE_STATUS == NetworkTask.ERROR_CODE_STATUS.ERROR)
                                        {
                                            __instance.isError = true;
                                        }
                                        if (error_CODE_STATUS == NetworkTask.ERROR_CODE_STATUS.ERROR_TO_MAINTENANCE_POPUP && __instance.lastRequestTask.CallbackOnFailure != null)
                                        {
                                            __instance.lastRequestTask.CallbackOnFailure(NetworkTask.ResultCode.Maintenance);
                                        }
                                        if (error_CODE_STATUS == NetworkTask.ERROR_CODE_STATUS.ERROR && __instance.lastRequestTask.CallbackOnFailure != null)
                                        {
                                            __instance.lastRequestTask.CallbackOnFailure(NetworkTask.ResultCode.Title);
                                        }
                                    }
                                }
                                goto IL_0838;
                            }
                            catch (Exception ex2)
                            {
                                __instance.disposeUnityWebRequest(unityWebRequest);
                                if (!__instance.lastRequestTask.GetType().Equals(typeof(CheckSpecialTitleTask)))
                                {
                                    string text3 = "NetworkManager Connect Error 2：";
                                    Exception ex3 = ex2;
                                    LocalLog.AccumulateTraceLog(text3 + ((ex3 != null) ? ex3.ToString() : null));
                                    throw ex2;
                                }
                                goto IL_0838;
                            }
                        }
                        LocalLog.AccumulateTraceLog("NetworkManager Connect Error 3");
                    }
                }
            IL_0838:
                __instance.ClearLastRequestTask();
                __instance.disposeUnityWebRequest(unityWebRequest);
                __instance.isConnect = false;
            }
            //UnityWebRequest unityWebRequest = null;
            yield break;
        }
    }
}
