using System;
using System.Globalization;
using UnityEngine;
using Wizard.RoomMatch;

namespace Shadowbus
{
    internal sealed class P2PRoomLifeUI : MonoBehaviour
    {
        private const string ObjectName = "ShadowbusInitialMaxLife";
        private const float FirstTurnScale = 0.74f;
        private const float FirstTurnVerticalOffset = 30f;
        private const float InputBelowFirstTurn = 90f;
        private const int FrameWidth = 220;
        private const int FrameHeight = 44;

        private GameObject root;
        private UIInputWizard input;
        private UILabel valueLabel;
        private Collider inputCollider;
        private Collider2D inputCollider2D;
        private bool editable;
        private int displayedValue = -1;

        internal static void Attach(RoomUIBase room)
        {
            if (room == null)
            {
                Plugin.Logger.LogWarning(
                    "[P2P] Cannot prepare the initial maximum life UI: room is null.");
                return;
            }
            if (room.GetComponent<P2PRoomLifeUI>() != null)
            {
                return;
            }

            string missingField = GetMissingField(room);
            if (missingField != null)
            {
                Plugin.Logger.LogWarning(
                    $"[P2P] Cannot prepare the initial maximum life UI for " +
                    $"{room.GetType().Name}: {missingField} is null.");
                return;
            }

            try
            {
                P2PRoomLifeUI controller = room.gameObject.AddComponent<P2PRoomLifeUI>();
                controller.Create(room);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "[P2P] Failed to create the initial maximum life input: " + ex);
            }
        }

        private void Create(RoomUIBase room)
        {
            UISprite frameSource = FindFrameSource(room);
            UILabel titleSource = FindTitleSource(room);
            Transform firstTurnTransform = room._firstTurnSelectRoot?.transform;
            Transform roomIdTransform = room.RoomIDBaseObject?.transform;
            Transform parent = firstTurnTransform?.parent ?? roomIdTransform.parent;

            root = NGUITools.AddChild(parent.gameObject);
            root.name = ObjectName;
            root.SetActive(false);

            if (firstTurnTransform != null)
            {
                Vector3 originalScale = firstTurnTransform.localScale;
                firstTurnTransform.localScale = new Vector3(
                    originalScale.x * FirstTurnScale,
                    originalScale.y * FirstTurnScale,
                    originalScale.z);
                firstTurnTransform.localPosition +=
                    new Vector3(0f, FirstTurnVerticalOffset, 0f);

                root.transform.localPosition = firstTurnTransform.localPosition +
                    new Vector3(0f, -InputBelowFirstTurn, 0f);
                root.transform.localRotation = firstTurnTransform.localRotation;
                root.transform.localScale = originalScale;
            }
            else
            {
                root.transform.localPosition =
                    parent.InverseTransformPoint(room.m_labelRoomID.transform.position) +
                    new Vector3(0f, -150f, 0f);
                root.transform.localRotation = roomIdTransform.localRotation;
                root.transform.localScale = roomIdTransform.localScale;
            }

            int depth = NGUITools.CalculateNextDepth(room.gameObject);

            UISprite frame = CloneWidget(
                frameSource,
                root,
                "InitialMaxLifeFrame");
            UILabel titleLabel = CloneWidget(
                titleSource,
                root,
                "InitialMaxLifeTitle");
            valueLabel = CloneWidget(
                room.m_labelRoomID,
                root,
                "InitialMaxLifeValue");

            RemoveInteractionComponents(frame.gameObject);

            frame.pivot = UIWidget.Pivot.Center;
            frame.width = FrameWidth;
            frame.height = FrameHeight;
            frame.depth = depth;
            frame.transform.localPosition = Vector3.zero;

            titleLabel.pivot = UIWidget.Pivot.Center;
            titleLabel.width = 130;
            titleLabel.height = 36;
            titleLabel.fontSize = 22;
            titleLabel.depth = depth + 1;
            titleLabel.transform.localPosition = new Vector3(-34f, 0f, 0f);

            valueLabel.pivot = UIWidget.Pivot.Center;
            valueLabel.width = 60;
            valueLabel.height = 36;
            valueLabel.fontSize = 28;
            valueLabel.depth = depth + 2;
            valueLabel.transform.localPosition = new Vector3(78f, 0f, 0f);

            DisableLocalization(titleLabel.gameObject);
            DisableLocalization(valueLabel.gameObject);
            titleLabel.text = "\u521d\u59cb\u751f\u547d";
            titleLabel.overflowMethod = UILabel.Overflow.ShrinkContent;
            titleLabel.maxLineCount = 1;
            valueLabel.overflowMethod = UILabel.Overflow.ShrinkContent;
            valueLabel.maxLineCount = 1;

            GameObject inputObject = frame.gameObject;
            input = inputObject.GetComponent<UIInputWizard>() ??
                inputObject.AddComponent<UIInputWizard>();
            input.label = valueLabel;
            input.validation = UIInput.Validation.Integer;
            input.keyboardType = UIInput.KeyboardType.NumberPad;
            input.characterLimit = 3;
            input.selectAllTextOnFocus = true;
            input.onValidate = ValidateDigit;
            input.onSubmit.Add(new EventDelegate(CommitValue));
            input.onDeselect.Add(new EventDelegate(CommitValue));

            NGUITools.AddWidgetCollider(inputObject);
            NGUITools.UpdateWidgetCollider(inputObject);
            inputCollider = inputObject.GetComponent<Collider>();
            inputCollider2D = inputObject.GetComponent<Collider2D>();

            displayedValue = P2PRuntime.InitialMaxLife;
            input.value = displayedValue.ToString(CultureInfo.InvariantCulture);
            root.SetActive(P2PRuntime.IsActive);
            RefreshEditableState();

            Plugin.Logger.LogInfo(
                $"[P2P] Prepared initial maximum life UI for " +
                $"{room.GetType().Name} from frame '{frameSource.name}'; " +
                $"active={P2PRuntime.IsActive}.");
        }

        private void Update()
        {
            if (input == null || root == null)
            {
                return;
            }

            if (root.activeSelf != P2PRuntime.IsActive)
            {
                root.SetActive(P2PRuntime.IsActive);
            }
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            RefreshEditableState();
            int currentValue = P2PRuntime.InitialMaxLife;
            if (currentValue != displayedValue && !input.isSelected)
            {
                displayedValue = currentValue;
                input.value = currentValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RefreshEditableState()
        {
            bool shouldBeEditable = P2PRuntime.CanEditRoomRules;
            if (editable == shouldBeEditable && input.enabled == shouldBeEditable)
            {
                return;
            }

            if (!shouldBeEditable && input.isSelected)
            {
                input.isSelected = false;
            }
            editable = shouldBeEditable;
            input.enabled = shouldBeEditable;
            if (inputCollider != null)
            {
                inputCollider.enabled = shouldBeEditable;
            }
            if (inputCollider2D != null)
            {
                inputCollider2D.enabled = shouldBeEditable;
            }
        }

        private void CommitValue()
        {
            if (input == null)
            {
                return;
            }

            int requested = P2PRuntime.InitialMaxLife;
            if (!string.IsNullOrEmpty(input.value))
            {
                int.TryParse(
                    input.value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out requested);
            }

            if (editable)
            {
                P2PRuntime.TrySetInitialMaxLife(requested);
            }
            displayedValue = P2PRuntime.InitialMaxLife;
            input.value = displayedValue.ToString(CultureInfo.InvariantCulture);
        }

        private static char ValidateDigit(string text, int charIndex, char addedChar)
        {
            return addedChar >= '0' && addedChar <= '9' ? addedChar : '\0';
        }

        private static T CloneWidget<T>(
            T source,
            GameObject root,
            string name)
            where T : UIWidget
        {
            GameObject cloneObject = UnityEngine.Object.Instantiate(source.gameObject);
            cloneObject.name = name;
            cloneObject.transform.SetParent(root.transform, false);
            cloneObject.transform.localPosition = Vector3.zero;
            cloneObject.transform.localRotation = Quaternion.identity;
            cloneObject.transform.localScale = Vector3.one;
            cloneObject.layer = root.layer;

            T clone = cloneObject.GetComponent<T>();
            ClearAnchors(clone);
            return clone;
        }

        private static string GetMissingField(RoomUIBase room)
        {
            if (room._firstTurnSelectRoot == null && room.RoomIDBaseObject == null)
            {
                return "both _firstTurnSelectRoot and RoomIDBaseObject";
            }
            Transform placementTransform =
                room._firstTurnSelectRoot?.transform ?? room.RoomIDBaseObject.transform;
            if (placementTransform.parent == null)
            {
                return "the placement transform parent";
            }
            if (room.m_labelRoomID == null)
            {
                return nameof(room.m_labelRoomID);
            }
            if (FindFrameSource(room) == null)
            {
                return "a usable UISprite frame source";
            }
            if (FindTitleSource(room) == null)
            {
                return "a usable UILabel title source";
            }
            return null;
        }

        private static UISprite FindFrameSource(RoomUIBase room)
        {
            if (room._roomIdFrame != null)
            {
                return room._roomIdFrame;
            }

            UISprite source = FindLargestSprite(room._firstTurnSelectRoot);
            return source ?? FindLargestSprite(room.RoomIDBaseObject);
        }

        private static UISprite FindLargestSprite(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return null;
            }

            UISprite largest = null;
            long largestArea = 0;
            foreach (UISprite sprite in
                rootObject.GetComponentsInChildren<UISprite>(true))
            {
                long area = (long)sprite.width * sprite.height;
                if (area > largestArea)
                {
                    largest = sprite;
                    largestArea = area;
                }
            }
            return largest;
        }

        private static UILabel FindTitleSource(RoomUIBase room)
        {
            if (room._roomIdTitleLabel != null)
            {
                return room._roomIdTitleLabel;
            }
            if (room._firstTurnLabel != null)
            {
                return room._firstTurnLabel;
            }

            foreach (UILabel label in
                room.RoomIDBaseObject.GetComponentsInChildren<UILabel>(true))
            {
                if (label != room.m_labelRoomID)
                {
                    return label;
                }
            }
            return room.m_labelRoomID;
        }

        private static void RemoveInteractionComponents(GameObject gameObject)
        {
            foreach (UIButton button in gameObject.GetComponents<UIButton>())
            {
                UnityEngine.Object.DestroyImmediate(button);
            }
            foreach (UIEventListener listener in
                gameObject.GetComponents<UIEventListener>())
            {
                UnityEngine.Object.DestroyImmediate(listener);
            }
            foreach (Collider collider in gameObject.GetComponents<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            foreach (Collider2D collider in gameObject.GetComponents<Collider2D>())
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void ClearAnchors(UIRect rect)
        {
            rect.leftAnchor.target = null;
            rect.rightAnchor.target = null;
            rect.bottomAnchor.target = null;
            rect.topAnchor.target = null;
            rect.ResetAnchors();
        }

        private static void DisableLocalization(GameObject gameObject)
        {
            foreach (UILocalize localize in
                gameObject.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }
        }
    }
}
