using System;
using UnityEngine;
using Wizard.RoomMatch;

namespace Shadowbus
{
    internal sealed class P2PRoomFormatUI : MonoBehaviour
    {
        private RoomUIBase room;
        private string displayedFormatId;

        internal static void Attach(RoomUIBase room)
        {
            if (room == null || room.GetComponent<P2PRoomFormatUI>() != null)
            {
                return;
            }

            P2PRoomFormatUI controller =
                room.gameObject.AddComponent<P2PRoomFormatUI>();
            controller.room = room;
            controller.RefreshTitle();
        }

        private void Update()
        {
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            if (!P2PRuntime.IsActive || room?.TopBar == null ||
                room.BattleParameterInstance == null)
            {
                return;
            }

            string formatId = CustomFormatContext.RoomFormatId;
            if (string.Equals(
                displayedFormatId,
                formatId,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string title = RoomRuleSetting.GetTopBarString(
                room.BattleParameterInstance,
                false);
            room.TopBar.SetTitleLabel(title);
            displayedFormatId = formatId;
            Plugin.Logger.LogInfo(
                $"[P2P] Updated room format display to " +
                $"{CustomFormatContext.RoomFormat.DisplayName} ({formatId}).");
        }
    }
}
