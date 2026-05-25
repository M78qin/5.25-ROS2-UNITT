using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin
{
    public sealed class TwinRuntimeUIBindings : MonoBehaviour
    {
        public GameObject CanvasRoot;
        public GameObject PanelRoot;
        public GameObject ContentRoot;
        public RectTransform ContentLayoutRoot;
        public ScrollRect ContentScrollRect;
        public TMP_Text TitleText;
        public TMP_Text HeaderStatusText;
        public TMP_Text HeaderModeText;
        public TMP_Text HeaderMotionText;
        public TMP_Text RecordBadgeText;
        public TMP_Text MinimizeText;
        public TMP_Text ConnectionText;
        public TMP_Text JointText;
        public TMP_Text ForceText;
        public TMP_Text MetricsText;
        public TMP_Text RecordText;
        public TMP_Text CommandText;
        public Button IdleButton;
        public Button Mode1Button;
        public Button PlanButton;
        public Button ExecuteButton;
        public Button HaltButton;
        public Button ConfirmExecuteButton;
        public Button CancelExecuteButton;
        public GameObject ConfirmPanel;
        public TMP_Text ConfirmText;
        public GameObject SliderGroup;
        public Slider[] JointSliders;
        public TMP_Text[] JointSliderLabels;
        public TwinRuntimeUICard ConnectionCard;
        public TwinRuntimeUICard JointsCard;
        public TwinRuntimeUICard ForceCard;
        public TwinRuntimeUICard MetricsCard;
        public TwinRuntimeUICard CommandCard;
        public TwinRuntimeUICard PlanCard;
        public TwinRuntimeUIPanelInteractor PanelInteractor;

        public bool Validate(out string[] missingFields)
        {
            List<string> missing = new List<string>();
            Check(missing, CanvasRoot, nameof(CanvasRoot));
            Check(missing, PanelRoot, nameof(PanelRoot));
            Check(missing, ContentLayoutRoot, nameof(ContentLayoutRoot));
            Check(missing, ContentScrollRect, nameof(ContentScrollRect));
            Check(missing, ConnectionText, nameof(ConnectionText));
            Check(missing, JointText, nameof(JointText));
            Check(missing, ForceText, nameof(ForceText));
            Check(missing, MetricsText, nameof(MetricsText));
            Check(missing, RecordText, nameof(RecordText));
            Check(missing, CommandText, nameof(CommandText));
            Check(missing, IdleButton, nameof(IdleButton));
            Check(missing, Mode1Button, nameof(Mode1Button));
            Check(missing, PlanButton, nameof(PlanButton));
            Check(missing, ExecuteButton, nameof(ExecuteButton));
            Check(missing, HaltButton, nameof(HaltButton));
            Check(missing, ConfirmExecuteButton, nameof(ConfirmExecuteButton));
            Check(missing, CancelExecuteButton, nameof(CancelExecuteButton));
            Check(missing, ConfirmPanel, nameof(ConfirmPanel));
            Check(missing, SliderGroup, nameof(SliderGroup));
            Check(missing, ConnectionCard, nameof(ConnectionCard));
            Check(missing, JointsCard, nameof(JointsCard));
            Check(missing, ForceCard, nameof(ForceCard));
            Check(missing, MetricsCard, nameof(MetricsCard));
            Check(missing, CommandCard, nameof(CommandCard));
            Check(missing, PlanCard, nameof(PlanCard));
            Check(missing, PanelInteractor, nameof(PanelInteractor));
            if (JointSliders == null || JointSliders.Length != 6) missing.Add(nameof(JointSliders));
            if (JointSliderLabels == null || JointSliderLabels.Length != 6) missing.Add(nameof(JointSliderLabels));
            missingFields = missing.ToArray();
            return missingFields.Length == 0;
        }

        private static void Check(List<string> missing, Object value, string name)
        {
            if (value == null) missing.Add(name);
        }
    }

    public static class TwinRuntimeUIFactory
    {
        public const string CanvasName = "DigitalTwinRuntimeCanvas";
        public const string PanelName = "DigitalTwinRuntimePanel";
        public const string DefaultRobotId = "H2515";
        public const float MinPanelWidth = 520f;
        public const float MinPanelHeight = 760f;
        public const float MaxPanelWidth = 760f;
        public const float MaxPanelHeight = 1200f;

        private static readonly Color PanelColor = new Color(0.035f, 0.043f, 0.052f, 0.88f);
        private static readonly Color CardColor = new Color(0.075f, 0.09f, 0.105f, 0.86f);
        private static readonly Color HeaderColor = new Color(0.105f, 0.125f, 0.145f, 0.92f);
        private static readonly Color TextColor = new Color(0.9f, 0.95f, 1f, 1f);
        private static readonly Color MutedTextColor = new Color(0.58f, 0.66f, 0.72f, 1f);
        private static readonly Color Cyan = new Color(0.1f, 0.72f, 0.88f, 1f);
        private static readonly Color Orange = new Color(1f, 0.58f, 0.18f, 1f);
        private static readonly Color Red = new Color(0.92f, 0.16f, 0.14f, 1f);
        private static readonly Color Green = new Color(0.38f, 0.82f, 0.42f, 1f);
        private const float HeaderHeight = 66f;
        private const float ContentTopOffset = 74f;
        private const float ContentBottomPadding = 22f;
        private const float CardTopPadding = 8f;
        private const float CardGap = 10f;
        private const float BridgeCardHeight = 118f;
        private const float JointsCardHeight = 154f;
        private const float ForceCardHeight = 122f;
        private const float MetricsCardHeight = 128f;
        private const float CommandCardHeight = 120f;
        private const float PlanCardHeight = 246f;

        public static string GetCanvasName(string robotId = DefaultRobotId) => $"{CleanRobotId(robotId)}_{CanvasName}";
        public static string GetPanelName(string robotId = DefaultRobotId) => $"{CleanRobotId(robotId)}_{PanelName}";

        public static TwinRuntimeUIBindings BuildHUD(TwinUIController controller, string robotId = DefaultRobotId)
        {
            Transform parent = controller != null && controller.transform.parent != null ? controller.transform.parent : null;
            TwinRuntimeUIBindings bindings = CreateRuntimeCanvas(new Vector2(-28f, -28f), new Vector2(520f, 920f), robotId, parent);
            controller?.ApplyGeneratedBindings(bindings);
            return bindings;
        }

        public static TwinRuntimeUIBindings CreateRuntimeCanvas(Vector2 panelPosition, Vector2 panelSize)
        {
            return CreateRuntimeCanvas(panelPosition, panelSize, DefaultRobotId, null);
        }

        public static TwinRuntimeUIBindings CreateRuntimeCanvas(Vector2 panelPosition, Vector2 panelSize, string robotId, Transform parent)
        {
            EnsureEventSystem();
            GameObject canvasGo = new GameObject(GetCanvasName(robotId), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null)
            {
                canvasGo.transform.SetParent(parent, false);
            }

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;

            TwinRuntimeUIBindings bindings = CreatePanel(canvasGo.transform, panelPosition, panelSize, robotId);
            bindings.CanvasRoot = canvasGo;
            return bindings;
        }

        public static TwinRuntimeUIBindings CreatePanel(Transform parent, Vector2 panelPosition, Vector2 panelSize)
        {
            return CreatePanel(parent, panelPosition, panelSize, DefaultRobotId);
        }

        public static TwinRuntimeUIBindings CreatePanel(Transform parent, Vector2 panelPosition, Vector2 panelSize, string robotId)
        {
            float panelWidth = Mathf.Clamp(panelSize.x, MinPanelWidth, MaxPanelWidth);
            float panelHeight = Mathf.Clamp(panelSize.y, MinPanelHeight, MaxPanelHeight);
            float cardWidth = panelWidth - 24f;
            float bodyWidth = cardWidth - 24f;

            GameObject panel = CreateRect(parent, GetPanelName(robotId), panelPosition, panelSize, new Vector2(1f, 1f), new Vector2(1f, 1f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelColor;
            Outline panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f);
            panelOutline.effectDistance = new Vector2(1f, -1f);

            TwinRuntimeUIBindings bindings = panel.AddComponent<TwinRuntimeUIBindings>();
            bindings.PanelRoot = panel;

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            GameObject dragHandle = CreateRect(panel.transform, "DragHandle", Vector2.zero, new Vector2(panelWidth, HeaderHeight), new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image dragImage = dragHandle.AddComponent<Image>();
            dragImage.color = HeaderColor;

            bindings.TitleText = CreateText(dragHandle.transform, "Title", new Vector2(18f, -8f), new Vector2(220f, 24f), 20, FontStyles.Bold, TextAlignmentOptions.Left);
            bindings.TitleText.text = $"{CleanRobotId(robotId)} DigitalTwin";
            TMP_Text subtitle = CreateText(dragHandle.transform, "Subtitle", new Vector2(18f, -32f), new Vector2(210f, 18f), 10, FontStyles.Normal, TextAlignmentOptions.Left);
            subtitle.text = "DartStudio Runtime HUD";
            subtitle.color = MutedTextColor;
            CreateText(dragHandle.transform, "DragDots", new Vector2(panelWidth - 172f, -8f), new Vector2(58f, 18f), 14, FontStyles.Bold, TextAlignmentOptions.Center).text = ":: ::";

            bindings.HeaderStatusText = CreatePill(dragHandle.transform, "HeaderStatus", "INIT", new Vector2(246f, -36f), new Vector2(78f, 20f), MutedTextColor);
            bindings.HeaderModeText = CreatePill(dragHandle.transform, "HeaderMode", "idle_stream", new Vector2(330f, -36f), new Vector2(106f, 20f), Cyan);
            bindings.HeaderMotionText = CreatePill(dragHandle.transform, "HeaderMotion", "IDLE", new Vector2(442f, -36f), new Vector2(54f, 20f), Green);
            bindings.RecordBadgeText = CreatePill(dragHandle.transform, "RecBadge", "", new Vector2(panelWidth - 142f, -10f), new Vector2(58f, 20f), Red);
            bindings.RecordBadgeText.gameObject.SetActive(false);

            Button pinButton = CreateButton(dragHandle.transform, "BtnPin", "PIN", new Vector2(panelWidth - 78f, -10f), new Vector2(40f, 28f), MutedTextColor);
            pinButton.interactable = false;
            Button minimizeButton = CreateButton(dragHandle.transform, "BtnMinimize", "-", new Vector2(panelWidth - 34f, -10f), new Vector2(24f, 28f), Cyan);
            bindings.MinimizeText = minimizeButton.GetComponentInChildren<TMP_Text>();

            float viewportHeight = panelHeight - (ContentTopOffset + ContentBottomPadding);
            GameObject content = CreateRect(panel.transform, "ContentRoot", new Vector2(0f, -ContentTopOffset), new Vector2(panelWidth, viewportHeight), new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image viewportImage = content.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            Mask viewportMask = content.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            ScrollRect contentScroll = content.AddComponent<ScrollRect>();
            contentScroll.horizontal = false;
            contentScroll.vertical = true;
            contentScroll.movementType = ScrollRect.MovementType.Clamped;
            contentScroll.scrollSensitivity = 28f;
            contentScroll.inertia = false;
            bindings.ContentRoot = content;
            bindings.ContentScrollRect = contentScroll;
            GameObject contentLayout = CreateRect(content.transform, "LayoutRoot", Vector2.zero, new Vector2(panelWidth, viewportHeight), new Vector2(0f, 1f), new Vector2(0f, 1f));
            bindings.ContentLayoutRoot = contentLayout.GetComponent<RectTransform>();
            contentScroll.viewport = content.GetComponent<RectTransform>();
            contentScroll.content = bindings.ContentLayoutRoot;

            float y = -8f;
            bindings.ConnectionCard = CreateCard(contentLayout.transform, "BridgeCard", "Bridge", "", new Vector2(12f, y), new Vector2(cardWidth, BridgeCardHeight), Cyan, out Transform bridgeBody);
            bindings.ConnectionText = CreateText(bridgeBody, "ConnectionText", new Vector2(12f, -10f), new Vector2(bodyWidth, 76f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.ConnectionText.text = "Connected  INIT\nSeq        --\nRate       -- Hz\nLatency    -- ms\nLast recv  -- ms";
            y -= 128f;

            bindings.JointsCard = CreateCard(contentLayout.transform, "JointsCard", "Joints", "LIVE", new Vector2(12f, y), new Vector2(cardWidth, JointsCardHeight), Cyan, out Transform jointsBody);
            bindings.JointText = CreateText(jointsBody, "JointText", new Vector2(12f, -10f), new Vector2(bodyWidth, 112f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.JointText.text = "J1   000.00 deg     J4   000.00 deg\nJ2   000.00 deg     J5   000.00 deg\nJ3   000.00 deg     J6   000.00 deg";
            y -= 164f;

            bindings.ForceCard = CreateCard(contentLayout.transform, "ForceCard", "Force / Torque", "", new Vector2(12f, y), new Vector2(cardWidth, ForceCardHeight), Cyan, out Transform forceBody);
            bindings.ForceText = CreateText(forceBody, "ForceText", new Vector2(12f, -10f), new Vector2(bodyWidth, 80f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.ForceText.text = "Force[N]        Torque[Nm]\nFx     0.000     Tx     0.000\nFy     0.000     Ty     0.000\nFz     0.000     Tz     0.000";
            y -= 132f;

            bindings.MetricsCard = CreateCard(contentLayout.transform, "MetricsCard", "Metrics / CSV", "", new Vector2(12f, y), new Vector2(cardWidth, MetricsCardHeight), Cyan, out Transform metricsBody);
            bindings.MetricsText = CreateText(metricsBody, "MetricsText", new Vector2(12f, -10f), new Vector2((bodyWidth - 12f) * 0.5f, 86f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.RecordText = CreateText(metricsBody, "RecordText", new Vector2(250f, -10f), new Vector2((bodyWidth - 12f) * 0.5f, 86f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.MetricsText.text = "Rate      -- Hz\nJitter    -- ms\nApply     -- ms\nDropped   --";
            bindings.RecordText.text = "CSV\nSession  --\nQueue    0\nState    idle";
            y -= 138f;

            bindings.CommandCard = CreateCard(contentLayout.transform, "CommandCard", "Command Safety", "", new Vector2(12f, y), new Vector2(cardWidth, CommandCardHeight), Orange, out Transform commandBody);
            bindings.CommandText = CreateText(commandBody, "CommandText", new Vector2(12f, -10f), new Vector2(bodyWidth, 80f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            bindings.CommandText.text = "BI        OFF      REAL      OFF      DRY-RUN   ON\nEmergency OFF\nLastCmd   --";
            y -= 130f;

            bindings.PlanCard = CreateCard(contentLayout.transform, "PlanCard", "Plan Execute", "PREVIEW", new Vector2(12f, y), new Vector2(cardWidth, PlanCardHeight), Orange, out Transform planBody);
            CreateCommandButtons(bindings, planBody);
            CreateJointSliders(bindings, planBody);

            CreateConfirmPanel(bindings, panel.transform);
            RectTransform resizeHandle = CreateResizeHandle(bindings, panel.transform);

            TwinRuntimeUIPanelInteractor interactor = panel.AddComponent<TwinRuntimeUIPanelInteractor>();
            interactor.Initialize(bindings, panelRect, dragHandle.GetComponent<RectTransform>(), resizeHandle, content, minimizeButton, bindings.MinimizeText, new Vector2(MinPanelWidth, MinPanelHeight), new Vector2(MaxPanelWidth, MaxPanelHeight));
            bindings.PanelInteractor = interactor;
            bindings.SliderGroup.SetActive(false);
            ApplyPanelLayout(bindings, panelRect.sizeDelta);
            if (bindings.ContentScrollRect != null)
            {
                bindings.ContentScrollRect.verticalNormalizedPosition = 1f;
            }
            return bindings;
        }

        public static void ApplyPanelLayout(TwinRuntimeUIBindings bindings, Vector2 panelSize)
        {
            if (bindings == null || bindings.PanelRoot == null)
            {
                return;
            }

            RectTransform panelRect = bindings.PanelRoot.GetComponent<RectTransform>();
            if (panelRect == null)
            {
                return;
            }

            float panelWidth = Mathf.Clamp(panelSize.x, MinPanelWidth, MaxPanelWidth);
            float panelHeight = Mathf.Clamp(panelSize.y, MinPanelHeight, MaxPanelHeight);
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            float cardWidth = panelWidth - 24f;
            float bodyWidth = cardWidth - 24f;
            float viewportHeight = Mathf.Max(260f, panelHeight - (ContentTopOffset + ContentBottomPadding));

            RectTransform dragHandle = FindRect(bindings.PanelRoot.transform, "DragHandle");
            SetRect(dragHandle, Vector2.zero, new Vector2(panelWidth, HeaderHeight));
            SetRect(FindRect(dragHandle, "DragDots"), new Vector2(panelWidth - 172f, -8f), new Vector2(58f, 18f));
            SetRect(FindRect(dragHandle, "BtnPin"), new Vector2(panelWidth - 78f, -10f), new Vector2(40f, 28f));
            SetRect(FindRect(dragHandle, "BtnMinimize"), new Vector2(panelWidth - 34f, -10f), new Vector2(24f, 28f));
            SetRect(GetRect(bindings.RecordBadgeText), new Vector2(panelWidth - 142f, -10f), new Vector2(58f, 20f));

            RectTransform contentViewport = GetRect(bindings.ContentRoot);
            SetRect(contentViewport, new Vector2(0f, -ContentTopOffset), new Vector2(panelWidth, viewportHeight));
            RectTransform contentLayout = bindings.ContentLayoutRoot;
            SetRect(contentLayout, Vector2.zero, new Vector2(panelWidth, viewportHeight));
            if (bindings.ContentScrollRect != null)
            {
                bindings.ContentScrollRect.viewport = contentViewport;
                bindings.ContentScrollRect.content = contentLayout;
            }

            float y = -CardTopPadding;
            LayoutCard(bindings.ConnectionCard, y, cardWidth, BridgeCardHeight);
            y -= BridgeCardHeight + CardGap;
            LayoutCard(bindings.JointsCard, y, cardWidth, JointsCardHeight);
            y -= JointsCardHeight + CardGap;
            LayoutCard(bindings.ForceCard, y, cardWidth, ForceCardHeight);
            y -= ForceCardHeight + CardGap;
            LayoutCard(bindings.MetricsCard, y, cardWidth, MetricsCardHeight);
            y -= MetricsCardHeight + CardGap;
            LayoutCard(bindings.CommandCard, y, cardWidth, CommandCardHeight);
            y -= CommandCardHeight + CardGap;
            LayoutCard(bindings.PlanCard, y, cardWidth, PlanCardHeight);

            SetRect(GetRect(bindings.ConnectionText), new Vector2(12f, -10f), new Vector2(bodyWidth, 76f));
            SetRect(GetRect(bindings.JointText), new Vector2(12f, -10f), new Vector2(bodyWidth, 112f));
            SetRect(GetRect(bindings.ForceText), new Vector2(12f, -10f), new Vector2(bodyWidth, 80f));
            float metricsColumnWidth = Mathf.Max(110f, (bodyWidth - 24f) * 0.5f);
            SetRect(GetRect(bindings.MetricsText), new Vector2(12f, -10f), new Vector2(metricsColumnWidth, 86f));
            SetRect(GetRect(bindings.RecordText), new Vector2(24f + metricsColumnWidth, -10f), new Vector2(metricsColumnWidth, 86f));
            SetRect(GetRect(bindings.CommandText), new Vector2(12f, -10f), new Vector2(bodyWidth, 80f));

            LayoutPlanControls(bindings, bodyWidth);

            float requiredContentHeight = CardTopPadding
                                          + BridgeCardHeight
                                          + JointsCardHeight
                                          + ForceCardHeight
                                          + MetricsCardHeight
                                          + CommandCardHeight
                                          + PlanCardHeight
                                          + CardGap * 5f
                                          + ContentBottomPadding;
            SetRect(contentLayout, Vector2.zero, new Vector2(panelWidth, Mathf.Max(viewportHeight, requiredContentHeight)));
        }

        public static GameObject EnsureSceneCanvas()
        {
            return EnsureSceneCanvas(DefaultRobotId, null);
        }

        public static GameObject EnsureSceneCanvas(string robotId, Transform parent)
        {
            EnsureEventSystem();
            string canvasName = GetCanvasName(robotId);
            GameObject existing = GameObject.Find(canvasName);
            if (existing != null)
            {
                return existing;
            }

            GameObject canvasGo = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) canvasGo.transform.SetParent(parent, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;
            return canvasGo;
        }

        private static void CreateCommandButtons(TwinRuntimeUIBindings bindings, Transform parent)
        {
            bindings.IdleButton = CreateButton(parent, "BtnIdle", "空闲同步", new Vector2(12f, -10f), new Vector2(82f, 34f), Cyan);
            bindings.Mode1Button = CreateButton(parent, "BtnMode1", "模式一测试", new Vector2(102f, -10f), new Vector2(104f, 34f), Cyan);
            bindings.PlanButton = CreateButton(parent, "BtnPlan", "双向控制", new Vector2(214f, -10f), new Vector2(94f, 34f), Orange);
            bindings.ExecuteButton = CreateButton(parent, "BtnExecute", "执行目标", new Vector2(316f, -10f), new Vector2(88f, 34f), Orange);
            bindings.HaltButton = CreateButton(parent, "BtnHalt", "急停", new Vector2(412f, -10f), new Vector2(58f, 34f), Red);
        }

        private static void CreateJointSliders(TwinRuntimeUIBindings bindings, Transform parent)
        {
            bindings.SliderGroup = CreateRect(parent, "JointTargetSliders", new Vector2(12f, -56f), new Vector2(458f, 162f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            bindings.JointSliders = new Slider[6];
            bindings.JointSliderLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++)
            {
                float y = -i * 25f;
                CreateText(bindings.SliderGroup.transform, $"Joint{i + 1}Min", new Vector2(0f, y), new Vector2(36f, 18f), 10, FontStyles.Normal, TextAlignmentOptions.Left).text = "-180";
                bindings.JointSliderLabels[i] = CreateText(bindings.SliderGroup.transform, $"Joint{i + 1}Label", new Vector2(42f, y), new Vector2(96f, 18f), 12, FontStyles.Normal, TextAlignmentOptions.Left);
                bindings.JointSliderLabels[i].text = $"J{i + 1}: 0.0 deg";
                bindings.JointSliders[i] = CreateSlider(bindings.SliderGroup.transform, $"Joint{i + 1}Slider", new Vector2(148f, y - 1f), new Vector2(300f, 18f));
                bindings.JointSliders[i].minValue = -360f;
                bindings.JointSliders[i].maxValue = 360f;
            }
        }

        private static TwinRuntimeUICard CreateCard(Transform parent, string name, string title, string tag, Vector2 pos, Vector2 size, Color accent, out Transform body)
        {
            GameObject card = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image image = card.AddComponent<Image>();
            image.color = CardColor;
            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
            outline.effectDistance = new Vector2(1f, -1f);

            GameObject header = CreateRect(card.transform, "Header", Vector2.zero, new Vector2(size.x, 30f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            header.AddComponent<Image>().color = HeaderColor;
            Image accentImage = CreateRect(header.transform, "Accent", new Vector2(0f, 0f), new Vector2(4f, 30f), new Vector2(0f, 1f), new Vector2(0f, 1f)).AddComponent<Image>();
            accentImage.color = accent;
            TMP_Text titleText = CreateText(header.transform, "Title", new Vector2(12f, -6f), new Vector2(160f, 18f), 13, FontStyles.Bold, TextAlignmentOptions.Left);
            titleText.text = title;
            TMP_Text tagText = CreatePill(header.transform, "Tag", tag, new Vector2(176f, -6f), new Vector2(66f, 18f), accent);
            TMP_Text chevronText = CreateText(header.transform, "Chevron", new Vector2(size.x - 28f, -6f), new Vector2(18f, 18f), 12, FontStyles.Bold, TextAlignmentOptions.Center);
            Button toggle = CreateTransparentButton(header.transform, "Toggle", new Vector2(size.x - 34f, -2f), new Vector2(28f, 24f));
            body = CreateRect(card.transform, "Body", new Vector2(0f, -32f), new Vector2(size.x, size.y - 34f), new Vector2(0f, 1f), new Vector2(0f, 1f)).transform;

            TwinRuntimeUICard runtimeCard = card.AddComponent<TwinRuntimeUICard>();
            runtimeCard.Initialize(header, body.gameObject, titleText, tagText, chevronText, accentImage, toggle);
            return runtimeCard;
        }

        private static void CreateConfirmPanel(TwinRuntimeUIBindings bindings, Transform parent)
        {
            GameObject panel = CreateRect(parent, "RealExecuteConfirmPanel", new Vector2(-16f, -220f), new Vector2(392f, 190f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            panel.AddComponent<Image>().color = new Color(0.055f, 0.035f, 0.035f, 0.96f);
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = Red;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            bindings.ConfirmPanel = panel;
            bindings.ConfirmText = CreateText(panel.transform, "ConfirmText", new Vector2(18f, -18f), new Vector2(356f, 92f), 14, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            bindings.ConfirmText.text = "REAL EXECUTE 确认\n将向真实机械臂发送 MOVE_JOINT。\n确认安全空间后再继续。";
            bindings.ConfirmExecuteButton = CreateButton(panel.transform, "BtnConfirmRealExecute", "确认执行", new Vector2(174f, -140f), new Vector2(96f, 32f), Red);
            bindings.CancelExecuteButton = CreateButton(panel.transform, "BtnCancelRealExecute", "取消", new Vector2(278f, -140f), new Vector2(78f, 32f), MutedTextColor);
            panel.SetActive(false);
        }

        private static RectTransform CreateResizeHandle(TwinRuntimeUIBindings bindings, Transform parent)
        {
            GameObject handle = CreateRect(parent, "ResizeHandle", new Vector2(-30f, 30f), new Vector2(28f, 28f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            handle.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            TMP_Text icon = CreateText(handle.transform, "Icon", Vector2.zero, new Vector2(28f, 28f), 20, FontStyles.Bold, TextAlignmentOptions.Center);
            icon.text = "///";
            icon.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f);
            return handle.GetComponent<RectTransform>();
        }

        private static void LayoutCard(TwinRuntimeUICard card, float y, float width, float height)
        {
            if (card == null)
            {
                return;
            }

            RectTransform cardRect = card.GetComponent<RectTransform>();
            SetRect(cardRect, new Vector2(12f, y), new Vector2(width, height));
            RectTransform headerRect = FindRect(cardRect, "Header");
            SetRect(headerRect, Vector2.zero, new Vector2(width, 30f));
            SetRect(FindRect(headerRect, "Chevron"), new Vector2(width - 28f, -6f), new Vector2(18f, 18f));
            SetRect(FindRect(headerRect, "Toggle"), new Vector2(width - 34f, -2f), new Vector2(28f, 24f));
            RectTransform bodyRect = FindRect(cardRect, "Body");
            SetRect(bodyRect, new Vector2(0f, -32f), new Vector2(width, height - 34f));
        }

        private static void LayoutPlanControls(TwinRuntimeUIBindings bindings, float bodyWidth)
        {
            float commandRowWidth = 82f + 104f + 94f + 88f + 58f + 8f * 4f;
            float x = 12f + Mathf.Max(0f, (bodyWidth - commandRowWidth) * 0.5f);
            x = PositionButton(bindings.IdleButton, x, 82f);
            x = PositionButton(bindings.Mode1Button, x, 104f);
            x = PositionButton(bindings.PlanButton, x, 94f);
            x = PositionButton(bindings.ExecuteButton, x, 88f);
            PositionButton(bindings.HaltButton, x, 58f);

            RectTransform sliderGroup = GetRect(bindings.SliderGroup);
            float sliderGroupWidth = Mathf.Max(300f, bodyWidth - 14f);
            SetRect(sliderGroup, new Vector2(12f, -56f), new Vector2(sliderGroupWidth, 162f));
            float sliderWidth = Mathf.Max(150f, sliderGroupWidth - 170f);
            for (int i = 0; i < 6; i++)
            {
                float rowY = -i * 25f;
                SetRect(FindRect(sliderGroup, $"Joint{i + 1}Min"), new Vector2(0f, rowY), new Vector2(36f, 18f));
                SetRect(GetRect(bindings.JointSliderLabels != null && i < bindings.JointSliderLabels.Length ? bindings.JointSliderLabels[i] : null), new Vector2(42f, rowY), new Vector2(108f, 18f));
                LayoutSlider(bindings.JointSliders != null && i < bindings.JointSliders.Length ? bindings.JointSliders[i] : null, new Vector2(160f, rowY - 1f), sliderWidth, 18f);
            }
        }

        private static float PositionButton(Button button, float x, float width)
        {
            SetRect(GetRect(button), new Vector2(x, -10f), new Vector2(width, 34f));
            return x + width + 8f;
        }

        private static void LayoutSlider(Slider slider, Vector2 position, float width, float height)
        {
            RectTransform root = GetRect(slider);
            SetRect(root, position, new Vector2(width, height));
            SetRect(FindRect(root, "Background"), Vector2.zero, new Vector2(width, height));
            float fillWidth = Mathf.Max(8f, width - 8f);
            float fillHeight = Mathf.Max(6f, height - 6f);
            RectTransform fillArea = FindRect(root, "Fill Area");
            SetRect(fillArea, new Vector2(4f, -3f), new Vector2(fillWidth, fillHeight));
            SetRect(FindRect(fillArea, "Fill"), Vector2.zero, new Vector2(fillWidth, fillHeight));
            SetRect(FindRect(root, "Handle Slide Area"), Vector2.zero, new Vector2(width, height));
        }

        private static RectTransform FindRect(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Transform child = parent.Find(name);
            return child == null ? null : child as RectTransform;
        }

        private static RectTransform GetRect(Component component)
        {
            return component == null ? null : component.transform as RectTransform;
        }

        private static RectTransform GetRect(GameObject gameObject)
        {
            return gameObject == null ? null : gameObject.transform as RectTransform;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static GameObject CreateRect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2 anchor, Vector2 pivot)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return go;
        }

        private static TMP_Text CreateText(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize, FontStyles style, TextAlignmentOptions alignment)
        {
            GameObject go = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = TextColor;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private static TMP_Text CreatePill(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image image = go.AddComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0.23f);
            TMP_Text text = CreateText(go.transform, "Label", Vector2.zero, size, 10, FontStyles.Bold, TextAlignmentOptions.Center);
            text.color = color;
            text.text = label;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image image = go.AddComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0.48f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(color.r, color.g, color.b, 0.72f);
            colors.pressedColor = new Color(color.r, color.g, color.b, 0.9f);
            button.colors = colors;

            TMP_Text text = CreateText(go.transform, "Label", Vector2.zero, size, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            text.text = label;
            text.color = Color.white;
            return button;
        }

        private static Button CreateTransparentButton(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Image image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject root = CreateRect(parent, name, pos, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Slider slider = root.AddComponent<Slider>();

            GameObject background = CreateRect(root.transform, "Background", Vector2.zero, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            background.AddComponent<Image>().color = new Color(0.16f, 0.18f, 0.2f, 1f);

            GameObject fillArea = CreateRect(root.transform, "Fill Area", new Vector2(4f, -3f), new Vector2(size.x - 8f, size.y - 6f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            GameObject fill = CreateRect(fillArea.transform, "Fill", Vector2.zero, fillArea.GetComponent<RectTransform>().sizeDelta, new Vector2(0f, 1f), new Vector2(0f, 1f));
            fill.AddComponent<Image>().color = new Color(Orange.r, Orange.g, Orange.b, 0.95f);

            GameObject handleArea = CreateRect(root.transform, "Handle Slide Area", Vector2.zero, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
            GameObject handle = CreateRect(handleArea.transform, "Handle", Vector2.zero, new Vector2(10f, 22f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
            handle.AddComponent<Image>().color = new Color(1f, 0.72f, 0.22f, 1f);

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = false;
            return slider;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static TMP_FontAsset ResolveFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        private static string CleanRobotId(string robotId)
        {
            return string.IsNullOrWhiteSpace(robotId) ? DefaultRobotId : robotId.Trim().Replace(" ", "_");
        }
    }
}
