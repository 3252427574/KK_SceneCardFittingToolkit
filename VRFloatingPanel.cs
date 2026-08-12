using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace KKPEHeightLock
{
    /// <summary>
    /// VR 悬浮菜单:完整复刻桌面端 ConfigPanel 的全部功能(场景播放器/服装预设/场景替换/场景工具/
    /// 身高锁定/界面缩放/VR 第一人称 + 选卡页 + 子目录页)。
    /// - 不跟随镜头:呼出时召唤到玩家面前一次,之后静止悬浮于世界(拖动标题栏可调整位置)
    /// - 左手柄 Y 键(ApplicationMenu)开合;右手射线命中高亮 + 扳机点击
    /// - 右摇杆上下滚动长列表;内容区用 RectMask2D 视口裁剪
    /// - 尺寸:0.8m × 1.05m,root localScale=0.001(1 UI 单位 = 1mm)
    /// </summary>
    public static class VRFloatingPanel
    {
        // ===== 面板尺寸(UI 单位,root localScale=0.001 → 1 单位 = 1mm)=====
        private const float UnitScale = 0.001f;
        private const float PanelW = 900f;        // 0.90m
        private const float PanelH = 1350f;       // 1.35m(加高,内容更宽松)
        private const float FontSize = 26f;       // 正文 26px = 0.026m
        private const float SmallFont = 20f;      // 辅助小字
        private const float BtnH = 48f;           // 按钮高
        private const float RowGap = 5f;          // 行间距
        private const float SectionH = 36f;       // 分区标题条高
        private const float Edge = 15f;           // 左右边距
        private const float TitleBarH = 66f;      // 标题栏高
        private const float BottomHintH = 36f;    // 底部提示高
        private const float ViewportH = PanelH - TitleBarH - BottomHintH - 30f; // 内容视口高
        private const int MainPageCount = 4;       // 主页面分页数
        private const int CardPageSize = 10;       // 选卡每页项数(文件夹+卡片)
        private const int SubdirPageSize = 12;     // 子目录每页行数
        private const int CharPageSize = 8;        // 场景人物每页人数
        private const float FollowDist = 1.4f;    // 呼出时距眼距离(米)
        private const float MaxRayDist = 8f;      // 射线最大长度(米)

        // ===== 颜色主题 =====
        private static readonly Color BgColor = new Color(0.10f, 0.11f, 0.15f, 0.95f);
        private static readonly Color TitleBarColor = new Color(0.16f, 0.30f, 0.55f, 1f);
        private static readonly Color SectionColor = new Color(0.20f, 0.22f, 0.30f, 0.92f);
        internal static readonly Color BtnNormal = new Color(0.22f, 0.27f, 0.38f, 1f);
        internal static readonly Color BtnHover = new Color(0.45f, 0.62f, 0.88f, 1f);
        internal static readonly Color BtnOn = new Color(0.14f, 0.55f, 0.28f, 1f);
        internal static readonly Color BtnOnHover = new Color(0.32f, 0.78f, 0.48f, 1f);
        internal static readonly Color DisabledColor = new Color(0.15f, 0.16f, 0.19f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color DimText = new Color(0.82f, 0.85f, 0.90f, 1f);

        // ===== 页面 =====
        private const int PageMain = 0;
        private const int PageCard = 1;
        private const int PageSubdir = 2;

        // ===== 运行时状态 =====
        private static bool _initialized;
        private static bool _visible;
        private static int _page = PageMain;
        private static GameObject _root;
        private static Transform _panelTf;
        private static Font _font;
        private static RectTransform _titleBarRect;
        private static LineRenderer _laser;      // 右手激光
        private static RectTransform _uiCursor;   // 菜单内光标(uGUI,命中面板时显示)

        // 页面容器
        private static RectTransform _viewport;
        private static readonly List<RectTransform> _mainPages = new List<RectTransform>(); // 主页面 5 个页容器
        private static RectTransform _contentCard;
        private static RectTransform _contentSubdir;

        // 页面固定行(视口内,不随列表滚动)
        private static RectTransform _cardTopBar;
        private static Text _cardHint;
        private static RectTransform _subdirTopBar;
        private static RectTransform _subdirBottomBar;

        // 分页状态(替代滚动)
        private static int _mainPageIndex;
        private static int _cardPageIndex;
        private static int _subdirPageIndex;
        // 翻页控件
        private static Text _mainPageTxt;
        private static VRPanelButton _mainPrevBtn, _mainNextBtn;
        private static Text _cardPageTxt;
        private static VRPanelButton _cardPrevBtn, _cardNextBtn;
        private static Text _subdirPageTxt;
        private static VRPanelButton _subdirPrevBtn, _subdirNextBtn;
        // 翻页行容器
        private static RectTransform _mainBottomBar;
        private static RectTransform _cardBottomBar;

        // 控件(全部页面,供命中检测)
        private static readonly List<VRPanelButton> _buttons = new List<VRPanelButton>();
        private static readonly List<Action> _refreshers = new List<Action>();

        // 部件三态展开
        private static bool _partsExpanded;

        // 页1 双视图(设置 / 场景人物列表)
        private static RectTransform _p1MainView;
        private static RectTransform _p1CharsView;
        private static float _charListViewTop;
        private static int _charPageIndex;
        private static Text _charPageTxt;
        private static VRPanelButton _charPrevBtn, _charNextBtn;
        private static Text _charEmptyHint;
        private static readonly List<VRPanelButton> _charRowButtons = new List<VRPanelButton>();
        private static readonly List<Text> _charRowTexts = new List<Text>();
        private static readonly List<Studio.OCIChar> _charRowChars = new List<Studio.OCIChar>();

        // ===== 选卡页状态 =====
        private class VRCardEntry
        {
            public string path;
            public string name;          // 角色名(懒加载)
            public bool metaLoaded;
            public Texture2D tex;        // 缩略图(懒加载)
            public bool texFailed;       // 缩略图加载失败标记(避免每帧重试)
        }
        private static bool _cardIsFemale;
        private static string _cardDir = "";          // 相对路径,"" = 根
        private static List<VRCardEntry> _cardEntries = new List<VRCardEntry>();
        private static List<string> _cardFolders = new List<string>();

        // ===== 子目录页状态 =====
        private static int _subdirMode;               // 0=场景 1=随机女 2=随机男 3=选卡
        private static string _subdirPath = "";
        private static List<string> _subdirList = new List<string>();

        // ==================================================================
        // 对外入口
        // ==================================================================

        public static void Toggle()
        {
            if (_visible) Hide();
            else Show();
        }

        public static bool IsVisible { get { return _visible; } }

        public static void Show()
        {
            EnsureInit();
            if (!_initialized || _root == null || _panelTf == null) return;
            _visible = true;
            _root.SetActive(true);
            // 不跟随:呼出时召唤到玩家面前一次,之后静止悬浮于世界
            PlaceInFront();
            SetPage(PageMain);
            KKPEHeightLockPlugin.Log.LogInfo($"VRFloatingPanel: 显示, 位置={_panelTf.position}, 旋转={_panelTf.rotation.eulerAngles}");
        }

        public static void Hide()
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
            foreach (var b in _buttons) b.SetHover(false);
        }

        /// <summary>每帧驱动(由 KKPEHeightLockPlugin.Update 调用)。</summary>
        public static void UpdatePanel()
        {
            if (!_visible || !_initialized || _panelTf == null) return;
            FollowHead();   // 菜单始终跟随眼前
            HandleRay();
            RefreshValues();
            if (_page == PageCard) LazyLoadCards(3);
        }

        /// <summary>每帧跟随头显:菜单固定悬浮在眼前正前方(头显移动到哪,菜单跟到哪)。
        /// 参考点用 VRGIN 的 VRCamera.Head(头显),与 Ermin 腕表一致,比 Camera.main 可靠。</summary>
        private static void FollowHead()
        {
            var head = GetHeadTransform();
            if (head == null) return;
            Vector3 target = head.position + head.forward * FollowDist + Vector3.up * -0.10f;
            _panelTf.position = Vector3.Lerp(_panelTf.position, target, 12f * Time.unscaledDeltaTime);
            Quaternion targetRot = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            _panelTf.rotation = Quaternion.Slerp(_panelTf.rotation, targetRot, 12f * Time.unscaledDeltaTime);
        }

        // ==================================================================
        // 位置(跟随头显)
        // ==================================================================

        private static void PlaceInFront()
        {
            var head = GetHeadTransform();
            if (head == null) return;
            _panelTf.position = head.position + head.forward * FollowDist + Vector3.up * -0.10f;
            _panelTf.rotation = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        }

        /// <summary>生成圆环光标纹理(亮环 + 半透明中心)。</summary>
        private static Sprite CreateCursorSprite()
        {
            try
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float cx = (size - 1) / 2f;
                float r = size / 2f - 1f;
                float ringR = r - 5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - cx;
                        float dy = y - cx;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        Color c = Color.clear;
                        if (d <= r && d >= ringR) c = new Color(1f, 1f, 1f, 1f);      // 亮环
                        else if (d < ringR) c = new Color(1f, 1f, 1f, 0.18f);          // 中心半透明
                        tex.SetPixel(x, y, c);
                    }
                }
                tex.Apply();
                return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>获取头显 transform:优先 VRGIN VRCamera.Head(与 Ermin 腕表同源),回退 PlayerCamera/Camera.main。</summary>
        private static Transform GetHeadTransform()
        {
            try
            {
                var vrcam = VRGIN.Core.VRCamera.Instance;
                if (vrcam != null && vrcam.Head != null) return vrcam.Head;
            }
            catch (Exception) { }
            try
            {
                var pc = UnityEngine.Object.FindObjectOfType<VRGIN.Visuals.PlayerCamera>();
                if (pc != null) return pc.transform;
            }
            catch (Exception) { }
            return Camera.main != null ? Camera.main.transform : null;
        }

        private static Camera GetVRCamera()
        {
            try
            {
                var pc = UnityEngine.Object.FindObjectOfType<VRGIN.Visuals.PlayerCamera>();
                if (pc != null)
                {
                    var c = pc.GetComponent<Camera>();
                    if (c != null) return c;
                }
            }
            catch (Exception) { }
            return Camera.main;
        }

        // ==================================================================
        // 手柄
        // ==================================================================

        private static Transform GetRightHand()
        {
            try
            {
                var mode = VRGIN.Core.VR.Mode;
                if (mode != null && mode.Right != null) return mode.Right.transform;
            }
            catch (Exception) { }
            return null;
        }

        private static SteamVR_Controller.Device GetRightDevice()
        {
            try
            {
                var mode = VRGIN.Core.VR.Mode;
                if (mode != null && mode.Right != null)
                {
                    var tracked = mode.Right.GetComponent<SteamVR_TrackedObject>();
                    if (tracked != null)
                    {
                        int idx = (int)tracked.index;
                        if (idx >= 0) return SteamVR_Controller.Input(idx);
                    }
                }
            }
            catch (Exception) { }
            try
            {
                int idx = SteamVR_Controller.GetDeviceIndex(SteamVR_Controller.DeviceRelation.Rightmost, Valve.VR.ETrackedDeviceClass.Controller, 0);
                if (idx < 0) return null;
                return SteamVR_Controller.Input(idx);
            }
            catch (Exception) { return null; }
        }

        // ==================================================================
        // 射线交互 + 滚动
        // ==================================================================

        private static void HandleRay()
        {
            var dev = GetRightDevice();
            var hand = GetRightHand();

            VRPanelButton hovered = null;
            bool hitPanel = false;
            Vector3 hitWorld = Vector3.zero;
            Vector3 rayEnd = Vector3.zero;

            if (hand != null)
            {
                Vector3 origin = hand.position;
                Vector3 dir = hand.forward;
                rayEnd = origin + dir * 1.5f;
                Ray ray = new Ray(origin, dir);
                Plane plane = new Plane(-_panelTf.forward, _panelTf.position);
                float dist;
                hitPanel = plane.Raycast(ray, out dist) && dist >= 0f && dist < MaxRayDist;
                if (hitPanel)
                {
                    hitWorld = ray.GetPoint(dist);
                    rayEnd = hitWorld;
                    for (int i = _buttons.Count - 1; i >= 0; i--)
                    {
                        var b = _buttons[i];
                        if (!b.Interactable || !b.gameObject.activeInHierarchy) continue;
                        Vector3 lp = b.rect.InverseTransformPoint(hitWorld);
                        if (b.rect.rect.Contains((Vector2)lp)) { hovered = b; break; }
                    }
                }
            }

            foreach (var b in _buttons) b.SetHover(b == hovered);

            if (_laser != null)
            {
                if (hand != null)
                {
                    _laser.enabled = true;
                    _laser.SetPosition(0, hand.position);
                    _laser.SetPosition(1, rayEnd);
                    Color lc = hitPanel ? new Color(0.5f, 1f, 0.9f, 1f) : new Color(0.4f, 0.9f, 1f, 0.55f);
                    _laser.SetColors(lc, lc);
                }
                else _laser.enabled = false;
            }

            // 菜单内光标:命中面板时显示在命中点(面板局部坐标),否则隐藏
            if (_uiCursor != null)
            {
                if (hitPanel)
                {
                    _uiCursor.gameObject.SetActive(true);
                    Vector3 lp = _panelTf.InverseTransformPoint(hitWorld);
                    _uiCursor.anchoredPosition = new Vector2(lp.x, lp.y);
                }
                else _uiCursor.gameObject.SetActive(false);
            }

            if (dev == null || !dev.valid)
            {
                return;
            }

            bool trigDown = dev.GetPressDown(SteamVR_Controller.ButtonMask.Trigger);

            if (trigDown)
            {
                if (hovered != null) hovered.Click();
            }
        }

        /// <summary>主页面翻页。</summary>
        private static void SetMainPage(int idx)
        {
            _mainPageIndex = Mathf.Clamp(idx, 0, MainPageCount - 1);
            for (int i = 0; i < _mainPages.Count; i++)
                if (_mainPages[i] != null) _mainPages[i].gameObject.SetActive(i == _mainPageIndex);
            if (_mainPageTxt != null) _mainPageTxt.text = "页 " + (_mainPageIndex + 1) + "/" + MainPageCount;
            if (_mainPrevBtn != null) _mainPrevBtn.interactable = _mainPageIndex > 0;
            if (_mainNextBtn != null) _mainNextBtn.interactable = _mainPageIndex < MainPageCount - 1;
        }

        /// <summary>选卡页翻页(仅改索引,列表由 RebuildCardRows 按页渲染)。</summary>
        private static void SetCardPage(int idx)
        {
            _cardPageIndex = idx;
            RebuildCardRows();
        }

        /// <summary>子目录页翻页(仅改索引,列表由 RebuildSubdirRows 按页渲染)。</summary>
        private static void SetSubdirPage(int idx)
        {
            _subdirPageIndex = idx;
            RebuildSubdirRows();
        }

        // ==================================================================
        // 值刷新
        // ==================================================================

        private static void RefreshValues()
        {
            try
            {
                var chara = ScenePlayerModule.CurrentEditChar();
                _coordCurrent = chara != null ? ScenePlayerModule.GetCoordinateType(chara) : -1;
            }
            catch (Exception) { }
            foreach (var r in _refreshers) { try { r(); } catch (Exception) { } }
            foreach (var b in _buttons) b.Refresh();
        }

        private static int _coordCurrent = -1;

        // ==================================================================
        // 初始化
        // ==================================================================

        private static void EnsureInit()
        {
            if (_initialized) return;
            try
            {
                _font = LoadFont();
                if (_font == null)
                    KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: 中文字体加载失败,面板文字可能无法显示");

                _root = new GameObject("KKPEHeightLock_VRPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                _root.layer = 0; // Default(与 Ermin 腕表一致,GuiLayer)
                var canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 30000; // 与 Ermin 腕表同层,确保不被游戏 UI/GUIQuad 遮挡
                var scaler = _root.GetComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 10f; // 与 Ermin 腕表一致
                _root.transform.localScale = new Vector3(UnitScale, UnitScale, UnitScale);
                UnityEngine.Object.DontDestroyOnLoad(_root);

                _panelTf = CreateRect("Panel", _root.transform, 0f, 0f, PanelW, PanelH);
                _panelTf.gameObject.AddComponent<Image>().color = BgColor;

                // 激光(原配置)
                try
                {
                    var laserGo = new GameObject("Laser");
                    laserGo.transform.SetParent(_root.transform, false);
                    _laser = laserGo.AddComponent<LineRenderer>();
                    _laser.useWorldSpace = true;
                    _laser.SetVertexCount(2);
                    _laser.SetWidth(0.0025f, 0.0025f);
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        var mat = new Material(shader);
                        mat.color = new Color(0.3f, 0.8f, 1f, 0.7f);
                        _laser.material = mat;
                    }
                    _laser.enabled = false;
                }
                catch (Exception) { }

                BuildLayout();

                _root.SetActive(false);
                _initialized = true;
                KKPEHeightLockPlugin.Log.LogInfo($"VRFloatingPanel: 初始化完成, 按钮={_buttons.Count}, 主页面={MainPageCount}页, 视口高={ViewportH}");
            }
            catch (Exception e)
            {
                // 失败:销毁已建对象,允许下次重试;UpdatePanel/Show 有空引用防护
                if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
                _panelTf = null;
                _initialized = false;
                KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: 初始化失败 " + e);
            }
        }

        // ==================================================================
        // 布局构建
        // ==================================================================

        private static void BuildLayout()
        {
            // ---- 标题栏 ----
            _titleBarRect = CreateRect("TitleBar", _panelTf, 0f, PanelH / 2f - TitleBarH / 2f, PanelW - Edge * 2f, TitleBarH);
            _titleBarRect.gameObject.AddComponent<Image>().color = TitleBarColor;
            AddText(_titleBarRect, "场景卡套档工具箱", Edge, 0f, PanelW - 140f, TitleBarH, 26f, TextAnchor.MiddleLeft, TextColor, true);
            AddButton(_titleBarRect, "×", (PanelW - Edge * 2f) / 2f - 40f, 0f, 56f, 44f, null, Toggle);

            // ---- 视口(内容裁剪:用 Image+Mask 模板裁剪,比 RectMask2D 在缩放 Canvas 下更可靠)----
            _viewport = CreateRect("Viewport", _panelTf, 0f, 0f, PanelW, ViewportH);
            var vpImg = _viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 1f); // 不透明,作为 mask 形状
            _viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            // ---- 菜单内光标(uGUI,跟随射线在面板上的命中点,便于选按钮)----
            _uiCursor = CreateRect("UICursor", _viewport, 0f, 0f, 44f, 44f);
            var cursorImg = _uiCursor.gameObject.AddComponent<Image>();
            cursorImg.sprite = CreateCursorSprite();
            cursorImg.color = new Color(0.3f, 1f, 0.8f, 1f);
            _uiCursor.gameObject.SetActive(false);

            // 主页面 5 个页容器 + 子页面容器
            for (int i = 0; i < MainPageCount; i++)
                _mainPages.Add(CreatePage("MainPage" + i, _viewport));
            _contentCard = CreatePage("ContentCard", _viewport);
            _contentSubdir = CreatePage("ContentSubdir", _viewport);

            // ---- 选卡页固定顶行(视口内)----
            _cardTopBar = CreateRect("CardTopBar", _viewport, 0f, ViewportH / 2f - 48f, PanelW, 48f);
            AddButton(_cardTopBar, "← 返回", -345f, 0f, 90f, 44f, null, () => SetPage(PageMain));
            var dirTxt = AddText(_cardTopBar, "", 60f, 0f, 300f, 44f, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => dirTxt.text = (_cardIsFemale ? "女卡: " : "男卡: ") + (string.IsNullOrEmpty(_cardDir) ? "(根)" : _cardDir));
            AddButton(_cardTopBar, "选子文件夹", 285f, 0f, 150f, 44f, null, () => OpenSubdirPicker(3));

            // ---- 子目录页固定顶行 / 底行(视口内)----
            _subdirTopBar = CreateRect("SubdirTopBar", _viewport, 0f, ViewportH / 2f - 48f, PanelW, 48f);
            AddButton(_subdirTopBar, "↑ 上级", -340f, 0f, 90f, 44f, null, () =>
            {
                int idx = _subdirPath.LastIndexOf('/');
                _subdirPath = idx >= 0 ? _subdirPath.Substring(0, idx) : "";
                RefreshSubdirList();
            });
            var subdirPathTxt = AddText(_subdirTopBar, "", 30f, 0f, 500f, 44f, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => subdirPathTxt.text = "路径: " + (string.IsNullOrEmpty(_subdirPath) ? "根目录" : _subdirPath));
            _subdirBottomBar = CreateRect("SubdirBottomBar", _viewport, 0f, -ViewportH / 2f + 48f, PanelW, 48f);
            // 翻页
            _subdirPrevBtn = AddButton(_subdirBottomBar, "◀", -295f, 0f, 70f, 44f, null, () => SetSubdirPage(_subdirPageIndex - 1));
            _subdirPageTxt = AddText(_subdirBottomBar, "页", -195f, 0f, 110f, 44f, SmallFont, TextAnchor.MiddleCenter, TextColor);
            _subdirNextBtn = AddButton(_subdirBottomBar, "▶", -95f, 0f, 70f, 44f, null, () => SetSubdirPage(_subdirPageIndex + 1));
            // 操作
            AddButton(_subdirBottomBar, "选择此目录", -40f, 0f, 140f, 44f, null, () =>
            {
                // mode 3(选卡)由本面板自管,不走 ScenePlayerModule/ConfigPanel
                if (_subdirMode != 3) ScenePlayerModule.ApplySubdir(_subdirMode, _subdirPath);
                ReturnFromSubdir();
            });
            AddButton(_subdirBottomBar, "根目录", 120f, 0f, 140f, 44f, null, () =>
            {
                if (_subdirMode != 3) ScenePlayerModule.ApplySubdir(_subdirMode, "");
                ReturnFromSubdir();
            });
            AddButton(_subdirBottomBar, "取消", 280f, 0f, 140f, 44f, null, ReturnFromSubdir);

            // ---- 主页面翻页行(视口底部固定)----
            _mainBottomBar = CreateRect("MainBottomBar", _viewport, 0f, -ViewportH / 2f + 48f, PanelW, 48f);
            _mainPrevBtn = AddButton(_mainBottomBar, "◀ 上一页", -230f, 0f, 170f, 44f, null, () => SetMainPage(_mainPageIndex - 1));
            _mainPageTxt = AddText(_mainBottomBar, "页", 0f, 0f, 200f, 44f, SmallFont, TextAnchor.MiddleCenter, TextColor);
            _mainNextBtn = AddButton(_mainBottomBar, "下一页 ▶", 230f, 0f, 170f, 44f, null, () => SetMainPage(_mainPageIndex + 1));

            // ---- 选卡页翻页行(视口底部固定)----
            _cardBottomBar = CreateRect("CardBottomBar", _viewport, 0f, -ViewportH / 2f + 48f, PanelW, 48f);
            _cardPrevBtn = AddButton(_cardBottomBar, "◀", -140f, 0f, 100f, 44f, null, () => SetCardPage(_cardPageIndex - 1));
            _cardPageTxt = AddText(_cardBottomBar, "页", 0f, 0f, 160f, 44f, SmallFont, TextAnchor.MiddleCenter, TextColor);
            _cardNextBtn = AddButton(_cardBottomBar, "▶", 140f, 0f, 100f, 44f, null, () => SetCardPage(_cardPageIndex + 1));
            _cardBottomBar.gameObject.SetActive(false);

            // ---- 主页面(分页构建)----
            BuildMainPages();
            // ---- 选卡页 / 子目录页(骨架)----
            BuildCardPage();
            BuildSubdirPage();

            // 默认显示主页面第 0 页
            SetMainPage(0);
            _contentCard.gameObject.SetActive(false);
            _contentSubdir.gameObject.SetActive(false);

            // 光标置于视口最上层(避免被内容/翻页行盖住)
            if (_uiCursor != null) _uiCursor.SetAsLastSibling();

            // ---- 底部操作提示 ----
            AddText(_panelTf, "左手Y开合 · 扳机点击 · ◀ ▶ 翻页", Edge, -PanelH / 2f + BottomHintH / 2f, PanelW - Edge * 2f, BottomHintH, SmallFont, TextAnchor.MiddleCenter, DimText);
        }

        /// <summary>创建分页容器(pivot/anchor 顶部,高 = 视口高,内容不滚动)。</summary>
        private static RectTransform CreatePage(string name, Transform parent)
        {
            var rt = CreateRect(name, parent, 0f, 0f, PanelW, ViewportH);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            return rt;
        }

        // ==================================================================
        // 主页面(完整复刻桌面端 ConfigPanel)
        // ==================================================================

        private static void BuildMainPages()
        {
            BuildMainPage0();
            BuildMainPage1();
            BuildMainPage2();
            BuildMainPage3();
        }

        /// <summary>页0:场景播放器 + VR 第一人称(设置视图);"场景人物列表 ▶"切换角色列表视图。</summary>
        private static void BuildMainPage0()
        {
            _p1MainView = CreateRect("P1Main", _mainPages[0], 0f, 0f, PanelW, ViewportH);
            _p1CharsView = CreateRect("P1Chars", _mainPages[0], 0f, 0f, PanelW, ViewportH);
            var p = _p1MainView;
            float y = -8f;
            AddSection(p, "场景播放器", ref y);
            AddButton(p, "◀", -340f, y, 70f, BtnH, null, ScenePlayerModule.LoadPrevScene);
            AddButton(p, "刷新", -262f, y, 94f, BtnH, null, ScenePlayerModule.RefreshSceneList);
            var sceneLabel = AddText(p, "", 15f, y, 300f, BtnH, SmallFont, TextAnchor.MiddleCenter, DimText);
            _refreshers.Add(() => sceneLabel.text = Truncate(ScenePlayerModule.SceneLabel(), 26));
            AddButton(p, "▶", 345f, y, 70f, BtnH, null, ScenePlayerModule.LoadNextScene);
            y -= BtnH + RowGap;
            AddButton(p, "选择子目录", -305f, y, 160f, BtnH, null, () => OpenSubdirPicker(0));
            var subdirLabel = AddText(p, "", 20f, y, 500f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => subdirLabel.text = "场景: " + ScenePlayerModule.SceneSubdirLabel());
            y -= BtnH + RowGap;
            AddButton(p, "删除场景", -335f, y, 110f, BtnH, null, () => ScenePlayerModule.DeleteCurrentScene(true));
            AddText(p, "上/下一场景并自动加载", 80f, y, 420f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            y -= BtnH + RowGap;
            AddButton(p, "随机女", -255f, y, 240f, BtnH, null, ScenePlayerModule.RandomReplaceFemale);
            AddButton(p, "随机男", 0f, y, 240f, BtnH, null, ScenePlayerModule.RandomReplaceMale);
            AddButton(p, "随机全部", 255f, y, 240f, BtnH, null, ScenePlayerModule.RandomReplaceAll);
            y -= BtnH + RowGap;
            AddButton(p, "女卡目录", -305f, y, 130f, BtnH, null, () => OpenSubdirPicker(1));
            var rfl = AddText(p, "", 20f, y, 480f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => rfl.text = "女卡: " + ScenePlayerModule.RandomFemaleSubdirLabel());
            y -= BtnH + RowGap;
            AddButton(p, "男卡目录", -305f, y, 130f, BtnH, null, () => OpenSubdirPicker(2));
            var rml = AddText(p, "", 20f, y, 480f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => rml.text = "男卡: " + ScenePlayerModule.RandomMaleSubdirLabel());
            y -= BtnH + RowGap;
            AddButton(p, "FSS", -340f, y, 60f, BtnH, null, ScenePlayerModule.RefreshFSS);
            AddButton(p, "◀", -272f, y, 44f, BtnH, null, () => ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex - 1));
            var fssLabel = AddText(p, "", 15f, y, 420f, BtnH, SmallFont, TextAnchor.MiddleCenter, DimText);
            _refreshers.Add(() => fssLabel.text = Truncate(ScenePlayerModule.FssLabel(), 26));
            AddButton(p, "▶", 345f, y, 44f, BtnH, null, () => ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex + 1));
            y -= BtnH + RowGap;
            var tlPlay = AddButton(p, "▶", -335f, y, 90f, BtnH, null, null);
            tlPlay.onClick = () =>
            {
                if (ScenePlayerModule.TimelinePlaying) ScenePlayerModule.TimelinePause();
                else ScenePlayerModule.TimelinePlay();
            };
            tlPlay.getText = () => ScenePlayerModule.TimelinePlaying ? "⏸" : "▶";
            AddButton(p, "⏹", -238f, y, 86f, BtnH, null, ScenePlayerModule.TimelineStop);
            var tlCam = AddButton(p, "CAM", -140f, y, 150f, BtnH, null, ScenePlayerModule.TimelineToggleCamera);
            tlCam.toggle = true;
            tlCam.getToggle = () => ScenePlayerModule.TimelineCameraControlled;
            tlCam.getText = () =>
            {
                if (!ScenePlayerModule.TimelineHasCameraKeyframe) return "CAM(无关键帧)";
                return ScenePlayerModule.TimelineCameraControlled ? "CAM ●" : "CAM ○";
            };
            _refreshers.Add(() => tlCam.interactable = ScenePlayerModule.TimelineHasCameraKeyframe);
            var tlLabel = AddText(p, "", 90f, y, 290f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => { try { tlLabel.text = Truncate(ScenePlayerModule.TimelineLabel(), 30); } catch (Exception) { } });
            y -= BtnH + RowGap;
            y -= 6f;

            // ---- VR 第一人称(与场景播放器同页)----
            AddSection(p, "VR 第一人称", ref y);
            var povBtn = AddButton(p, "", 0f, y, PanelW - Edge * 2f, BtnH, null, () =>
            {
                KKPEHeightLockPlugin.POVEnabled.Value = !KKPEHeightLockPlugin.POVEnabled.Value;
                KKPEHeightLockPlugin.Log.LogMessage("KKPEHeightLock: first-person POV " + (KKPEHeightLockPlugin.POVEnabled.Value ? "enabled" : "disabled"));
            });
            povBtn.getText = () => KKPEHeightLockPlugin.POVEnabled.Value ? "关闭 VR 第一人称" : "开启 VR 第一人称";
            povBtn.toggle = true;
            povBtn.getToggle = () => KKPEHeightLockPlugin.POVEnabled.Value;
            y -= BtnH + RowGap;
            AddStepRow(p, y, "高低", () => KKPEHeightLockPlugin.POVHeightOffset.Value, v => KKPEHeightLockPlugin.POVHeightOffset.Value = v, 0.05f, -1f, 1f, "0.00");
            y -= BtnH + RowGap;
            AddStepRow(p, y, "左右", () => KKPEHeightLockPlugin.POVLateralOffset.Value, v => KKPEHeightLockPlugin.POVLateralOffset.Value = v, 0.05f, -1f, 1f, "0.00");
            y -= BtnH + RowGap;
            AddStepRow(p, y, "前后", () => KKPEHeightLockPlugin.POVViewOffset.Value, v => KKPEHeightLockPlugin.POVViewOffset.Value = v, 0.05f, -1f, 1f, "0.00");
            y -= BtnH + RowGap;
            AddStepRow(p, y, "FOV", () => KKPEHeightLockPlugin.POVFOV.Value, v => KKPEHeightLockPlugin.POVFOV.Value = v, 5f, 30f, 150f, "0");
            y -= BtnH + RowGap;
            AddStepRow(p, y, "转头速度", () => KKPEHeightLockPlugin.POVLookSpeed.Value, v => KKPEHeightLockPlugin.POVLookSpeed.Value = v, 0.5f, 0.5f, 10f, "0.0");
            y -= BtnH + RowGap;
            AddButton(p, "场景人物列表 ▶", 0f, y, PanelW - Edge * 2f, BtnH, null, () => ShowCharList(true));

            // ---- 视图 B:场景人物列表(默认隐藏)----
            BuildCharListView();
            _p1CharsView.gameObject.SetActive(false);
        }

        // ==================================================================
        // 场景人物列表(页1 视图 B,链接工作室选中)
        // ==================================================================

        private static bool _charListViewShown; // 页1 是否处于角色列表视图

        private static void ShowCharList(bool show)
        {
            _charListViewShown = show;
            if (_p1MainView != null) _p1MainView.gameObject.SetActive(!show);
            if (_p1CharsView != null) _p1CharsView.gameObject.SetActive(show);
            // 角色列表视图有自己完整的翻页行,隐藏主页面底部翻页行,避免与列表内容重叠
            if (_mainBottomBar != null) _mainBottomBar.gameObject.SetActive(!show);
            if (show) RebuildCharRows();
        }

        private static void BuildCharListView()
        {
            var v = _p1CharsView;
            float y = -8f;
            AddButton(v, "← 返回设置", 0f, y, PanelW - Edge * 2f, 44f, null, () => ShowCharList(false));
            y -= 44f + RowGap;
            AddSection(v, "场景人物(点击选中,供第一人称/服装预设/替换使用)", ref y);
            _charListViewTop = y;
            // 角色行区(8 行) + 翻页行(预留空间)
            y -= CharPageSize * (BtnH + RowGap) + 10f;
            var pageBar = CreateRect("CharPageBar", v, 0f, y, PanelW, 44f);
            _charPrevBtn = AddButton(pageBar, "◀", -220f, 0f, 100f, 44f, null, () => SetCharPage(_charPageIndex - 1));
            _charPageTxt = AddText(pageBar, "页", -40f, 0f, 180f, 44f, SmallFont, TextAnchor.MiddleCenter, TextColor);
            _charNextBtn = AddButton(pageBar, "▶", 100f, 0f, 100f, 44f, null, () => SetCharPage(_charPageIndex + 1));
            AddButton(pageBar, "刷新", 230f, 0f, 140f, 44f, null, RebuildCharRows);
        }

        private static void SetCharPage(int idx)
        {
            _charPageIndex = idx;
            RebuildCharRows();
        }

        /// <summary>重建场景人物行(按子页渲染,选中状态由 getToggle 实时反映)。</summary>
        private static void RebuildCharRows()
        {
            foreach (var b in _charRowButtons) { _buttons.Remove(b); if (b != null) UnityEngine.Object.Destroy(b.gameObject); }
            foreach (var t in _charRowTexts) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            if (_charEmptyHint != null) { UnityEngine.Object.Destroy(_charEmptyHint.gameObject); _charEmptyHint = null; }
            _charRowButtons.Clear();
            _charRowTexts.Clear();
            _charRowChars.Clear();

            var chars = ScenePlayerModule.GetSceneCharacters();
            int total = chars.Count;
            int totalPages = Mathf.Max(1, (total + CharPageSize - 1) / CharPageSize);
            _charPageIndex = Mathf.Clamp(_charPageIndex, 0, totalPages - 1);
            if (_charPageTxt != null) _charPageTxt.text = "页 " + (_charPageIndex + 1) + "/" + totalPages;
            if (_charPrevBtn != null) _charPrevBtn.interactable = _charPageIndex > 0;
            if (_charNextBtn != null) _charNextBtn.interactable = _charPageIndex < totalPages - 1;

            float y = _charListViewTop;
            if (total == 0)
            {
                _charEmptyHint = AddText(_p1CharsView, "场景中没有角色", 0f, y, PanelW - Edge * 2f, 44f, SmallFont, TextAnchor.MiddleCenter, DimText);
                return;
            }

            int start = _charPageIndex * CharPageSize;
            int end = Mathf.Min(start + CharPageSize, total);
            for (int i = start; i < end; i++)
            {
                var c = chars[i];
                var nameTxt = AddText(_p1CharsView, CharName(c), -200f, y, 380f, BtnH, SmallFont, TextAnchor.MiddleLeft, TextColor);
                var selBtn = AddButton(_p1CharsView, "选中", 200f, y, 180f, BtnH, null, () => ScenePlayerModule.SelectCharacter(c));
                selBtn.toggle = true;
                selBtn.getToggle = () => ScenePlayerModule.IsCharacterSelected(c);
                _charRowButtons.Add(selBtn);
                _charRowTexts.Add(nameTxt);
                _charRowChars.Add(c);
                y -= BtnH + RowGap;
            }
            // 名字实时刷新
            _refreshers.Add(() =>
            {
                for (int i2 = 0; i2 < _charRowTexts.Count && i2 < _charRowChars.Count; i2++)
                    if (_charRowTexts[i2] != null && _charRowChars[i2] != null)
                        _charRowTexts[i2].text = CharName(_charRowChars[i2]);
            });
        }

        private static string CharName(Studio.OCIChar c)
        {
            try
            {
                if (c != null && c.charInfo != null && c.charInfo.fileParam != null && !string.IsNullOrEmpty(c.charInfo.fileParam.fullname))
                    return Truncate(c.charInfo.fileParam.fullname, 22);
            }
            catch (Exception) { }
            return "角色";
        }

        /// <summary>页1:服装预设 + 部件三态。</summary>
        private static void BuildMainPage1()
        {
            var p = _mainPages[1];
            float y = -8f;
            AddSection(p, "服装预设(选中角色)", ref y);
            AddCoordButton(p, "校服1", 0, -293f, y, 180f);
            AddCoordButton(p, "校服2", 1, -98f, y, 180f);
            AddCoordButton(p, "体操", 2, 98f, y, 180f);
            AddCoordButton(p, "泳装", 3, 293f, y, 180f);
            y -= BtnH + RowGap;
            AddCoordButton(p, "社团", 4, -250f, y, 240f);
            AddCoordButton(p, "便服", 5, 0f, y, 240f);
            AddCoordButton(p, "睡衣", 6, 250f, y, 240f);
            y -= BtnH + RowGap;
            var partsToggle = AddButton(p, "部件三态(展开/收起)", 0f, y, PanelW - Edge * 2f, BtnH, null, null);
            partsToggle.toggle = true;
            partsToggle.getToggle = () => _partsExpanded;
            partsToggle.setToggle = v => _partsExpanded = v;
            y -= BtnH + RowGap;
            string[] partNames = { "上衣", "下衣", "内衣", "内裤", "袜", "鞋" };
            int[] partIndexes = { 0, 1, 2, 3, 10, 11 };
            for (int i = 0; i < partNames.Length; i++)
            {
                int pi = partIndexes[i];
                var rowRt = CreateRect("PartRow" + i, p, 0f, y, PanelW - Edge * 2f, BtnH);
                AddText(rowRt, partNames[i], -330f, 0f, 70f, BtnH, FontSize, TextAnchor.MiddleLeft, TextColor);
                AddButton(rowRt, "穿", -215f, 0f, 150f, BtnH, null, () => ScenePlayerModule.SetClothesPart(ScenePlayerModule.CurrentEditChar(), pi, 0));
                AddButton(rowRt, "半脱", -35f, 0f, 150f, BtnH, null, () => ScenePlayerModule.SetClothesPart(ScenePlayerModule.CurrentEditChar(), pi, 1));
                AddButton(rowRt, "脱", 145f, 0f, 150f, BtnH, null, () => ScenePlayerModule.SetClothesPart(ScenePlayerModule.CurrentEditChar(), pi, 2));
                _partRows.Add(rowRt.gameObject);
                y -= BtnH + RowGap;
            }
            _refreshers.Add(() =>
            {
                foreach (var row in _partRows) if (row != null) row.SetActive(_partsExpanded);
            });
        }

        /// <summary>页2:场景替换。</summary>
        private static void BuildMainPage2()
        {
            var p = _mainPages[2];
            float y = -8f;
            AddSection(p, "场景替换角色", ref y);
            AddToggle(p, "自动替换", -255f, y, 240f, () => KKPEHeightLockPlugin.AutoReplaceOnLoad.Value, v => KKPEHeightLockPlugin.AutoReplaceOnLoad.Value = v);
            AddToggle(p, "替换女", 0f, y, 240f, () => KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value, v => KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value = v);
            AddToggle(p, "替换男", 255f, y, 240f, () => KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value, v => KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value = v);
            y -= BtnH + RowGap;
            AddToggle(p, "保留姿势", -190f, y, 360f, () => KKPEHeightLockPlugin.PreservePoseOnReplace.Value, v => KKPEHeightLockPlugin.PreservePoseOnReplace.Value = v);
            AddToggle(p, "保留服装", 190f, y, 360f, () => KKPEHeightLockPlugin.PreserveClothesOnReplace.Value, v => KKPEHeightLockPlugin.PreserveClothesOnReplace.Value = v);
            y -= BtnH + RowGap;
            var fCardTxt = AddText(p, "", -250f, y, 460f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => fCardTxt.text = "女卡: " + CardFileName(KKPEHeightLockPlugin.FemaleCardPath.Value));
            AddButton(p, "选择卡", 70f, y, 160f, BtnH, null, () => OpenCardPicker(true));
            y -= BtnH + RowGap;
            var mCardTxt = AddText(p, "", -250f, y, 460f, BtnH, SmallFont, TextAnchor.MiddleLeft, DimText);
            _refreshers.Add(() => mCardTxt.text = "男卡: " + CardFileName(KKPEHeightLockPlugin.MaleCardPath.Value));
            AddButton(p, "选择卡", 70f, y, 160f, BtnH, null, () => OpenCardPicker(false));
            y -= BtnH + RowGap;
            AddButton(p, "立即替换场景角色", 0f, y, PanelW - Edge * 2f, 58f, null, SceneReplacer.ReplaceAllCharacters);
        }

        /// <summary>页3:场景工具 + 身高锁定 + 界面缩放。</summary>
        private static void BuildMainPage3()
        {
            var p = _mainPages[3];
            float y = -8f;
            AddSection(p, "场景工具", ref y);
            AddButton(p, "一键消除无用球体", -190f, y, 360f, BtnH, null, SceneTools.RemoveUselessSpheres);
            AddButton(p, "一键去马赛克", 190f, y, 360f, BtnH, null, SceneTools.DecensorScene);
            y -= BtnH + RowGap;
            AddToggle(p, "自动去球", -190f, y, 360f, () => KKPEHeightLockPlugin.AutoRemoveSpheres.Value, v => KKPEHeightLockPlugin.AutoRemoveSpheres.Value = v);
            AddToggle(p, "自动去码", 190f, y, 360f, () => KKPEHeightLockPlugin.AutoDecensor.Value, v => KKPEHeightLockPlugin.AutoDecensor.Value = v);
            y -= BtnH + RowGap;
            y -= 6f;
            AddSection(p, "身高 / 身材锁定", ref y);
            AddToggle(p, "启用锁定", 0f, y, PanelW - Edge * 2f, () => KKPEHeightLockPlugin.Enabled.Value, v => KKPEHeightLockPlugin.Enabled.Value = v);
            y -= BtnH + RowGap;
            AddModeButton(p, "仅身高骨骼", 0, -255f, y, 240f);
            AddModeButton(p, "仅体型滑块", 1, 0f, y, 240f);
            AddModeButton(p, "全部体型", 2, 255f, y, 240f);
            y -= BtnH + RowGap;
            y -= 6f;
            AddSection(p, "界面缩放(VR)", ref y);
            AddStepRow(p, y, "缩放", () => KKPEHeightLockPlugin.UIScale.Value, v => KKPEHeightLockPlugin.UIScale.Value = v, 0.1f, 0.5f, 2.5f, "0.0");
        }

        // ==================================================================
        // 选卡页
        // ==================================================================

        private static void OpenCardPicker(bool isFemale)
        {
            _cardIsFemale = isFemale;
            _cardDir = "";
            _cardPageIndex = 0;
            RefreshCardList();
            SetPage(PageCard);
        }

        private static void BuildCardPage()
        {
            // 提示行(固定,顶行下方)
            _cardHint = AddText(_viewport, "◀ ▶ 翻页浏览 · 卡片操作:选择(设替换卡)/直接替换(全部选中)/替换选中(单个)",
                0f, ViewportH / 2f - 104f, PanelW - Edge * 2f, 40f, SmallFont, TextAnchor.MiddleCenter, DimText);
            // 列表容器(随 content 滚动;y=-90 让出固定顶行+hint,避免遮挡)
            _cardList = CreateRect("CardList", _contentCard, 0f, -90f, PanelW, 100f);
        }

        private static RectTransform _cardList;
        private static Text _cardEmptyHint;
        private static List<VRPanelButton> _cardRowButtons = new List<VRPanelButton>();
        private static List<RawImage> _cardRowImages = new List<RawImage>();
        private static List<Text> _cardRowTexts = new List<Text>();
        private static List<VRCardEntry> _cardRowEntries = new List<VRCardEntry>();

        private static void RefreshCardList()
        {
            _cardEntries = new List<VRCardEntry>();
            _cardFolders = new List<string>();
            try
            {
                string baseDir = Path.Combine(UserData.Path, _cardIsFemale ? "chara/female" : "chara/male");
                string currentDir = string.IsNullOrEmpty(_cardDir) ? baseDir : Path.Combine(baseDir, _cardDir);
                foreach (var d in Directory.GetDirectories(currentDir))
                    _cardFolders.Add(Path.GetFileName(d));
                _cardFolders.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(currentDir, "*.png"))
                    _cardEntries.Add(new VRCardEntry { path = f });
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: RefreshCardList error: " + e);
            }
            RebuildCardRows();
        }

        /// <summary>重建卡片行(文件夹行 + 卡片行)。</summary>
        private static void RebuildCardRows()
        {
            // 清理旧行(按钮 + 缩略图/文本对象 + 纹理)
            foreach (var b in _cardRowButtons) { _buttons.Remove(b); if (b != null) UnityEngine.Object.Destroy(b.gameObject); }
            foreach (var img in _cardRowImages) if (img != null) UnityEngine.Object.Destroy(img.gameObject);
            foreach (var t in _cardRowTexts) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            foreach (var e in _cardRowEntries) if (e != null && e.tex != null) { UnityEngine.Object.Destroy(e.tex); e.tex = null; }
            if (_cardEmptyHint != null) { UnityEngine.Object.Destroy(_cardEmptyHint.gameObject); _cardEmptyHint = null; }
            _cardRowButtons.Clear();
            _cardRowImages.Clear();
            _cardRowTexts.Clear();
            _cardRowEntries.Clear();

            float y = _cardList.anchoredPosition.y; // 容器顶部(-90,让出固定顶行+hint)
            bool any = false;

            // 分页:文件夹 + 卡片 合并计项
            int totalItems = _cardFolders.Count + _cardEntries.Count;
            int totalPages = Mathf.Max(1, (totalItems + CardPageSize - 1) / CardPageSize);
            _cardPageIndex = Mathf.Clamp(_cardPageIndex, 0, totalPages - 1);
            if (_cardPageTxt != null) _cardPageTxt.text = "页 " + (_cardPageIndex + 1) + "/" + totalPages;
            if (_cardPrevBtn != null) _cardPrevBtn.interactable = _cardPageIndex > 0;
            if (_cardNextBtn != null) _cardNextBtn.interactable = _cardPageIndex < totalPages - 1;
            int start = _cardPageIndex * CardPageSize;
            int end = Mathf.Min(start + CardPageSize, totalItems);
            int pos = 0;

            // 文件夹行
            foreach (var sub in _cardFolders)
            {
                if (pos >= start && pos < end)
                {
                    string s = sub;
                    var b = AddButton(_cardList, "📁 " + Truncate(s, 24), 0f, y, PanelW - Edge * 2f, 44f, null, () =>
                    {
                        _cardDir = string.IsNullOrEmpty(_cardDir) ? s : _cardDir + "/" + s;
                        _cardPageIndex = 0;
                        RefreshCardList();
                    });
                    _cardRowButtons.Add(b);
                    y -= 44f + RowGap;
                    any = true;
                }
                pos++;
            }

            // 卡片行
            foreach (var entry in _cardEntries)
            {
                if (pos >= start && pos < end)
                {
                    var img = AddRawImage(_cardList, y, 56f);
                    var nameTxt = AddText(_cardList, entry.name ?? Path.GetFileNameWithoutExtension(entry.path), -200f, y, 190f, 56f, SmallFont, TextAnchor.MiddleLeft, TextColor);
                    var selBtn = AddButton(_cardList, "选择", -60f, y, 90f, 56f, null, () =>
                    {
                        if (_cardIsFemale) KKPEHeightLockPlugin.FemaleCardPath.Value = entry.path;
                        else KKPEHeightLockPlugin.MaleCardPath.Value = entry.path;
                        KKPEHeightLockPlugin.Log.LogMessage("KKPEHeightLock: " + (_cardIsFemale ? "female" : "male") + " card set to " + entry.path);
                        SetPage(PageMain);
                    });
                    var repBtn = AddButton(_cardList, "直接替换", 50f, y, 110f, 56f, null, () =>
                    {
                        int replaced = ConfigPanel.ReplaceSelectedCharacter(entry.path);
                        if (replaced > 0)
                        {
                            KKPEHeightLockPlugin.Log.LogMessage("KKPEHeightLock: replaced " + replaced + " selected character(s)");
                            SetPage(PageMain);
                        }
                        else KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: no character selected, nothing to replace");
                    });
                    var repOneBtn = AddButton(_cardList, "替换选中", 165f, y, 110f, 56f, null, () =>
                    {
                        if (ConfigPanel.ReplaceSingleSelectedCharacter(entry.path))
                            SetPage(PageMain);
                    });
                    _cardRowButtons.Add(selBtn);
                    _cardRowButtons.Add(repBtn);
                    _cardRowButtons.Add(repOneBtn);
                    _cardRowImages.Add(img);
                    _cardRowTexts.Add(nameTxt);
                    _cardRowEntries.Add(entry);
                    y -= 56f + RowGap;
                    any = true;
                }
                pos++;
            }

            if (!any)
                _cardEmptyHint = AddText(_cardList, "(此目录下没有角色卡)", 0f, y, PanelW - Edge * 2f, 44f, SmallFont, TextAnchor.MiddleCenter, DimText);
        }

        /// <summary>懒加载角色名与缩略图(每帧若干,避免卡顿)。</summary>
        private static void LazyLoadCards(int perFrame)
        {
            int processed = 0;
            for (int i = 0; i < _cardRowEntries.Count && processed < perFrame; i++)
            {
                var e = _cardRowEntries[i];
                if (!e.metaLoaded)
                {
                    try
                    {
                        var chaFile = new ChaFileControl();
                        if (chaFile.LoadCharaFile(e.path, 255, true, true))
                            e.name = chaFile.parameter != null && !string.IsNullOrEmpty(chaFile.parameter.fullname)
                                ? chaFile.parameter.fullname
                                : Path.GetFileNameWithoutExtension(e.path);
                        else e.name = Path.GetFileNameWithoutExtension(e.path);
                    }
                    catch (Exception) { e.name = Path.GetFileNameWithoutExtension(e.path); }
                    e.metaLoaded = true;
                    if (i < _cardRowTexts.Count) _cardRowTexts[i].text = Truncate(e.name, 22);
                    processed++;
                    continue;
                }
                if (e.tex == null && !e.texFailed)
                {
                    try { e.tex = PngAssist.LoadTexture(e.path); }
                    catch (Exception) { e.texFailed = true; }
                    if (e.tex == null) e.texFailed = true;
                    if (i < _cardRowImages.Count && e.tex != null) _cardRowImages[i].texture = e.tex;
                    processed++;
                }
            }
        }

        // ==================================================================
        // 子目录页
        // ==================================================================

        private static void OpenSubdirPicker(int mode)
        {
            _subdirMode = mode;
            _subdirPath = "";
            _subdirPageIndex = 0;
            RefreshSubdirList();
            SetPage(PageSubdir);
        }

        private static void RefreshSubdirList()
        {
            // mode 3(选卡)由本面板自管目录,不经过 ScenePlayerModule(它绑定 ConfigPanel 状态)
            _subdirList = _subdirMode == 3 ? ListCardSubdirs(_subdirPath) : ScenePlayerModule.ListSubdirsAt(_subdirMode, _subdirPath);
            RebuildSubdirRows();
        }

        /// <summary>选卡模式(3):列出选卡根目录下指定相对路径的直接子文件夹。</summary>
        private static List<string> ListCardSubdirs(string path)
        {
            var result = new List<string>();
            try
            {
                string baseDir = Path.Combine(UserData.Path, _cardIsFemale ? "chara/female" : "chara/male");
                string dir = string.IsNullOrEmpty(path) ? baseDir : Path.Combine(baseDir, path);
                foreach (var d in Directory.GetDirectories(dir))
                    result.Add(Path.GetFileName(d));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            return result;
        }

        private static RectTransform _subdirListRect;
        private static Text _subdirEmptyHint;
        private static List<VRPanelButton> _subdirRowButtons = new List<VRPanelButton>();

        private static void BuildSubdirPage()
        {
            // 列表容器(随 content 滚动;y=-48 让出固定顶行,避免遮挡)
            _subdirListRect = CreateRect("SubdirList", _contentSubdir, 0f, -48f, PanelW, 100f);
        }

        private static void ReturnFromSubdir()
        {
            if (_subdirMode == 3)
            {
                // 从选卡页进入:选中的路径写回选卡目录并回到选卡页
                _cardDir = _subdirPath;
                SetPage(PageCard);
                RefreshCardList();
            }
            else
            {
                SetPage(PageMain);
            }
        }

        private static void RebuildSubdirRows()
        {
            foreach (var b in _subdirRowButtons) { _buttons.Remove(b); if (b != null) UnityEngine.Object.Destroy(b.gameObject); }
            if (_subdirEmptyHint != null) { UnityEngine.Object.Destroy(_subdirEmptyHint.gameObject); _subdirEmptyHint = null; }
            _subdirRowButtons.Clear();

            float y = _subdirListRect.anchoredPosition.y; // 容器顶部(-48,让出固定顶行)

            // 分页
            int totalPages = Mathf.Max(1, (_subdirList.Count + SubdirPageSize - 1) / SubdirPageSize);
            _subdirPageIndex = Mathf.Clamp(_subdirPageIndex, 0, totalPages - 1);
            if (_subdirPageTxt != null) _subdirPageTxt.text = "页 " + (_subdirPageIndex + 1) + "/" + totalPages;
            if (_subdirPrevBtn != null) _subdirPrevBtn.interactable = _subdirPageIndex > 0;
            if (_subdirNextBtn != null) _subdirNextBtn.interactable = _subdirPageIndex < totalPages - 1;
            int start = _subdirPageIndex * SubdirPageSize;
            int end = Mathf.Min(start + SubdirPageSize, _subdirList.Count);

            if (_subdirList.Count == 0)
            {
                _subdirEmptyHint = AddText(_subdirListRect, "(此目录下没有子文件夹)", 0f, y, PanelW - Edge * 2f, 44f, SmallFont, TextAnchor.MiddleCenter, DimText);
            }
            else
            {
                for (int i = start; i < end; i++)
                {
                    string s = _subdirList[i];
                    var b = AddButton(_subdirListRect, "📁 " + Truncate(s, 26), 0f, y, PanelW - Edge * 2f, 44f, null, () =>
                    {
                        _subdirPath = string.IsNullOrEmpty(_subdirPath) ? s : _subdirPath + "/" + s;
                        _subdirPageIndex = 0;
                        RefreshSubdirList();
                    });
                    _subdirRowButtons.Add(b);
                    y -= 44f + RowGap;
                }
            }
        }

        // ==================================================================
        // 页面切换
        // ==================================================================

        private static void SetPage(int page)
        {
            _page = page;
            // 回到主页面:重置页1为设置视图(角色列表视图随主页面隐藏)
            if (page == PageMain) ShowCharList(false);
            // 主页面页容器:仅主页面显示当前页,其余全部隐藏(避免与子页面重叠)
            for (int i = 0; i < _mainPages.Count; i++)
                if (_mainPages[i] != null) _mainPages[i].gameObject.SetActive(page == PageMain && i == _mainPageIndex);
            if (_contentCard != null) _contentCard.gameObject.SetActive(page == PageCard);
            if (_contentSubdir != null) _contentSubdir.gameObject.SetActive(page == PageSubdir);
            // 固定行按页显示
            if (_cardTopBar != null) _cardTopBar.gameObject.SetActive(page == PageCard);
            if (_cardHint != null) _cardHint.gameObject.SetActive(page == PageCard);
            if (_subdirTopBar != null) _subdirTopBar.gameObject.SetActive(page == PageSubdir);
            if (_subdirBottomBar != null) _subdirBottomBar.gameObject.SetActive(page == PageSubdir);
            if (_mainBottomBar != null) _mainBottomBar.gameObject.SetActive(page == PageMain && !_charListViewShown);
            if (_cardBottomBar != null) _cardBottomBar.gameObject.SetActive(page == PageCard);
            // 清空 hover
            foreach (var b in _buttons) b.SetHover(false);
        }

        // ==================================================================
        // UI 构建助手
        // ==================================================================

        private static RectTransform CreateRect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (IsScrollContainer(parent))
            {
                // 滚动容器内的元素:顶部锚定,y = 距容器顶部的距离(向下为负),与 content 的滚动坐标自洽
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        /// <summary>判断是否为分页/列表容器(其子元素需顶部锚定,保证 y 为距顶部距离)。</summary>
        private static bool IsScrollContainer(Transform parent)
        {
            if (parent == _contentCard || parent == _contentSubdir
                || parent == _cardList || parent == _subdirListRect
                || parent == _p1MainView || parent == _p1CharsView) return true;
            foreach (var p in _mainPages) if (parent == p) return true;
            return false;
        }

        private static Text AddText(Transform parent, string text, float x, float y, float w, float h,
            float fontSize, TextAnchor align, Color color, bool outline = false)
        {
            var rt = CreateRect("Text", parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = Mathf.RoundToInt(fontSize);
            t.alignment = align;
            t.color = color;
            t.text = text ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.75f);
                o.effectDistance = new Vector2(2f, -2f);
            }
            return t;
        }

        private static RawImage AddRawImage(Transform parent, float y, float size)
        {
            var rt = CreateRect("Thumb", parent, -342f, y, size, size);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.color = Color.white;
            return img;
        }

        private static VRPanelButton AddButton(Transform parent, string text, float x, float y, float w, float h,
            Action onClick, Action action2 = null)
        {
            var rt = CreateRect("Btn", parent, x, y, w, h);
            rt.gameObject.AddComponent<Image>().color = BtnNormal;
            var b = rt.gameObject.AddComponent<VRPanelButton>();
            b.rect = rt;
            b.bg = rt.GetComponent<Image>();
            b.label = AddText(rt, text ?? "", 0f, 0f, w, h, FontSize, TextAnchor.MiddleCenter, TextColor);
            b.onClick = onClick ?? action2;
            _buttons.Add(b);
            return b;
        }

        private static void AddSection(Transform parent, string title, ref float y)
        {
            var rt = CreateRect("Section", parent, 0f, y, PanelW - Edge * 2f, SectionH);
            rt.gameObject.AddComponent<Image>().color = SectionColor;
            AddText(rt, title, Edge, 0f, PanelW - Edge * 4f, SectionH, SmallFont, TextAnchor.MiddleLeft, DimText);
            y -= SectionH + RowGap;
        }

        /// <summary>行起始(内容坐标以容器中心为 0;此函数保留,便于将来调整行内布局)。</summary>
        private static void AddRowStart(Transform parent, float y) { }

        private static void AddToggle(Transform parent, string text, float x, float y, float w,
            Func<bool> get, Action<bool> set)
        {
            var b = AddButton(parent, text, x, y, w, BtnH, null, null);
            b.toggle = true;
            b.getToggle = get;
            b.setToggle = set;
        }

        private static void AddModeButton(Transform parent, string text, int mode, float x, float y, float w)
        {
            var b = AddButton(parent, text, x, y, w, BtnH, null, null);
            b.toggle = true;
            b.getToggle = () => KKPEHeightLockPlugin.LockMode.Value == (BodyLockMode)mode;
            b.setToggle = v => { if (v) KKPEHeightLockPlugin.LockMode.Value = (BodyLockMode)mode; };
        }

        private static void AddCoordButton(Transform parent, string text, int coordType, float x, float y, float w)
        {
            var b = AddButton(parent, text, x, y, w, BtnH, null, () =>
            {
                var c = ScenePlayerModule.CurrentEditChar();
                if (c != null) ScenePlayerModule.SetCoordinateType(c, coordType);
            });
            b.toggle = true;
            b.getToggle = () => _coordCurrent == coordType;
        }

        private static void AddStepRow(Transform parent, float y, string label, Func<float> get, Action<float> set,
            float step, float min, float max, string fmt)
        {
            const float labelW = 150f;
            const float btnW = 90f;
            const float valW = 170f;
            float left = -PanelW / 2f + Edge;
            AddText(parent, label, left + labelW / 2f, y, labelW, BtnH, FontSize, TextAnchor.MiddleLeft, TextColor);
            float cx = left + labelW;
            var minus = AddButton(parent, "−", cx + btnW / 2f, y, btnW, BtnH, null, null);
            minus.onClick = () => set(Mathf.Clamp(get() - step, min, max));
            var valTxt = AddText(parent, "", cx + btnW + valW / 2f, y, valW, BtnH, FontSize, TextAnchor.MiddleCenter, TextColor);
            var plus = AddButton(parent, "+", cx + btnW + valW + btnW / 2f, y, btnW, BtnH, null, null);
            plus.onClick = () => set(Mathf.Clamp(get() + step, min, max));
            _refreshers.Add(() => { try { valTxt.text = get().ToString(fmt); } catch (Exception) { } });
        }

        private static readonly List<GameObject> _partRows = new List<GameObject>();

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }

        private static string CardFileName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "(未设置)";
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return "(未设置)";
                return Truncate(name, 26);
            }
            catch (Exception) { return "(未设置)"; }
        }

        private static Font LoadFont()
        {
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
                if (f != null)
                {
                    KKPEHeightLockPlugin.Log.LogInfo("VRFloatingPanel: 使用动态字体 " + f.name);
                    return f;
                }
                KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: 动态字体创建返回 null, 回退 Arial");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: 动态字体创建失败 " + e.Message);
            }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("VRFloatingPanel: Arial 加载失败 " + e.Message); return null; }
        }
    }

    /// <summary>VR 悬浮面板按钮组件:自实现 hover/toggle,由 VRFloatingPanel 的射线检测驱动。</summary>
    internal sealed class VRPanelButton : MonoBehaviour
    {
        public RectTransform rect;
        public Image bg;
        public Text label;
        public Color normal = VRFloatingPanel.BtnNormal;
        public Color hover = VRFloatingPanel.BtnHover;
        public Color on = VRFloatingPanel.BtnOn;
        public Color onHover = VRFloatingPanel.BtnOnHover;
        public bool toggle;
        public Func<bool> getToggle;
        public Action<bool> setToggle;
        public Func<string> getText;
        public Action onClick;
        public bool interactable = true;

        private bool _hovered;
        private bool _toggleOn;

        public bool Interactable { get { return interactable; } }

        /// <summary>把按钮挂到别的父节点下(标题栏等)。</summary>
        public VRPanelButton SetParent2(Transform parent)
        {
            rect.SetParent(parent, false);
            return this;
        }

        public void SetHover(bool h)
        {
            if (_hovered == h) return;
            _hovered = h;
            ApplyColor();
        }

        public void Refresh()
        {
            if (toggle && getToggle != null)
            {
                try { _toggleOn = getToggle(); }
                catch (Exception) { }
            }
            if (getText != null && label != null)
            {
                try { label.text = getText(); }
                catch (Exception) { }
            }
            ApplyColor();
        }

        public void Click()
        {
            if (!interactable) return;
            try
            {
                if (toggle && setToggle != null && getToggle != null)
                    setToggle(!getToggle());
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRPanelButton toggle error: " + e.Message);
            }
            try { if (onClick != null) onClick(); }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRPanelButton click error: " + e.Message);
            }
            Refresh();
        }

        private void ApplyColor()
        {
            if (bg == null) return;
            if (!interactable)
            {
                bg.color = VRFloatingPanel.DisabledColor;
                return;
            }
            if (toggle)
                bg.color = _toggleOn ? (_hovered ? onHover : on) : (_hovered ? hover : normal);
            else
                bg.color = _hovered ? hover : normal;
        }
    }
}
