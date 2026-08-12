using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HSPE.AMModules;
using KKAPI.Studio.SaveLoad;
using UnityEngine;

namespace KKPEHeightLock
{
    public enum BodyLockMode
    {
        HeightOnly = 0,   // 只锁定身高骨骼(cf_n_height),不保留身材参数
        ShapeOnly = 1,    // 只保留体型滑块 shapeValueBody
        AllBody = 2       // 保留全部体型参数(滑块 + 乳量物理 + 骨架类型)
    }

    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("CharaStudio.exe")]
    public class KKPEHeightLockPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.kkpeheightlock";
        public const string PluginName = "Scene Card Fitting Toolkit";
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;
        internal static KKPEHeightLockPlugin Instance;
        internal static ConfigEntry<string> BoneName;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<BodyLockMode> LockMode;

        // 场景加载自动替换角色
        internal static ConfigEntry<bool> AutoReplaceOnLoad;
        internal static ConfigEntry<bool> ReplaceFemaleOnLoad;
        internal static ConfigEntry<bool> ReplaceMaleOnLoad;
        internal static ConfigEntry<string> FemaleCardPath;
        internal static ConfigEntry<string> MaleCardPath;
        internal static ConfigEntry<KeyboardShortcut> ReplaceKey;

        // 场景工具:去无用球体 / 去码
        internal static ConfigEntry<bool> AutoRemoveSpheres;
        internal static ConfigEntry<bool> AutoDecensor;

        // 场景播放器(ScenePlayer 复刻)
        internal static ConfigEntry<string> SceneDir;
        internal static ConfigEntry<string> RandomFemaleDir;
        internal static ConfigEntry<string> RandomMaleDir;
        internal static ConfigEntry<bool> RetainHeightOnRandom;

        // 替换角色时保留原场景状态
        internal static ConfigEntry<bool> PreservePoseOnReplace;
        internal static ConfigEntry<bool> PreserveClothesOnReplace;

        // UI 缩放(VR 用)
        internal static ConfigEntry<float> UIScale;

        // 第一人称 POV
        internal static ConfigEntry<bool> POVEnabled;
        internal static ConfigEntry<KeyboardShortcut> POVToggleKey;
        internal static ConfigEntry<float> POVViewOffset;
        internal static ConfigEntry<float> POVFOV;
        internal static ConfigEntry<float> POVLookSpeed;
        internal static ConfigEntry<float> POVHeightOffset;
        internal static ConfigEntry<float> POVLateralOffset;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            BoneName = Config.Bind("General", "BoneName", "cf_n_height",
                "要自动锁定缩放的身高骨骼名(在 cf_j_root 下)。变体如 cf_n_heightD / cf_n_heightE 也可填。");
            Enabled = Config.Bind("General", "Enabled", true,
                "是否启用本插件功能。");
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F10),
                "在游戏内切换启用开关的快捷键。");
            LockMode = Config.Bind("General", "LockMode", BodyLockMode.HeightOnly,
                "身材锁定模式:\n" +
                "HeightOnly = 只锁定身高骨骼缩放(cf_n_height),不保留身材参数\n" +
                "ShapeOnly = 变更角色时只保留体型滑块(身高/三围等 shapeValueBody)\n" +
                "AllBody = 变更角色时保留全部体型参数(体型滑块 + 胸部软硬度 + 骨架类型)");

            AutoReplaceOnLoad = Config.Bind("SceneReplace", "AutoReplaceOnLoad", false,
                "加载场景卡后,自动把场景中的角色替换为指定角色卡。");
            ReplaceFemaleOnLoad = Config.Bind("SceneReplace", "ReplaceFemaleOnLoad", true,
                "自动替换时是否替换女角色(需 AutoReplaceOnLoad 开启)。");
            ReplaceMaleOnLoad = Config.Bind("SceneReplace", "ReplaceMaleOnLoad", true,
                "自动替换时是否替换男角色(需 AutoReplaceOnLoad 开启)。");
            FemaleCardPath = Config.Bind("SceneReplace", "FemaleCardPath", "",
                "女角色替换卡路径(角色卡 .png 文件)。留空 = 不替换女角色。");
            MaleCardPath = Config.Bind("SceneReplace", "MaleCardPath", "",
                "男角色替换卡路径(角色卡 .png 文件)。留空 = 不替换男角色。");
            ReplaceKey = Config.Bind("SceneReplace", "ReplaceKey", new KeyboardShortcut(KeyCode.F11),
                "在游戏内手动触发一次按性别替换角色的快捷键。");

            AutoRemoveSpheres = Config.Bind("SceneTools", "AutoRemoveSpheres", false,
                "加载场景后自动隐藏无用球体(灯光/阴影辅助球)。");
            AutoDecensor = Config.Bind("SceneTools", "AutoDecensor", false,
                "加载场景后自动去码(移除马赛克/圣光材质)。");

            SceneDir = Config.Bind("ScenePlayer", "SceneDir", "UserData/studio/scene",
                "场景播放器扫描的场景目录(默认 UserData/studio/scene)。");
            RandomFemaleDir = Config.Bind("ScenePlayer", "RandomFemaleDir", "",
                "随机替换女角色卡的目录。留空 = 用 UserData/chara/female。");
            RandomMaleDir = Config.Bind("ScenePlayer", "RandomMaleDir", "",
                "随机替换男角色卡的目录。留空 = 用 UserData/chara/male。");
            RetainHeightOnRandom = Config.Bind("ScenePlayer", "RetainHeightOnRandom", true,
                "随机替换角色时保留原身高(防穿模)。");

            PreservePoseOnReplace = Config.Bind("SceneReplace", "PreservePoseOnReplace", true,
                "替换角色后恢复原场景的姿势/骨骼变换(只换外观,姿势不变)。");
            PreserveClothesOnReplace = Config.Bind("SceneReplace", "PreserveClothesOnReplace", false,
                "替换角色后恢复原场景的服装(只换脸/身材,服装不变)。");

            UIScale = Config.Bind("General", "UIScale", 1.5f,
                "菜单界面和按钮的整体缩放倍率。VR 建议 1.5~2.0,桌面用 1.0。");

            POVEnabled = Config.Bind("POV", "POVEnabled", false,
                "是否启用 VR 第一人称视角(需选中一个角色,仅 VR 模式)。");
            POVToggleKey = Config.Bind("POV", "POVToggleKey", new KeyboardShortcut(KeyCode.F12),
                "在 VR 中切换第一人称的快捷键。");
            POVViewOffset = Config.Bind("POV", "POVViewOffset", 0f,
                "视角前后偏移(相对头部,正值向前)。");
            POVFOV = Config.Bind("POV", "POVFOV", 75f,
                "第一人称视野角度 FOV。");
            POVLookSpeed = Config.Bind("POV", "POVLookSpeed", 2.5f,
                "右手摇杆转头灵敏度。");
            POVHeightOffset = Config.Bind("POV", "POVHeightOffset", 0f,
                "摄像头垂直高低偏移(相对眼睛位置,正值向上,负值向下)。");
            POVLateralOffset = Config.Bind("POV", "POVLateralOffset", 0f,
                "摄像头左右偏移(相对眼睛位置,正值向右,负值向左)。");

            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(KKPEHeightLockPlugin).Assembly);
            PatchErminThumbstickIsolation(harmony);

            // 渲染前最后一刻(SteamVR 追踪/各插件移动都执行完后)钉住第一人称 rig,
            // 否则头显/原点会被 SteamVR 的 pose 更新覆盖,俯仰转头时头显位置飘
            try
            {
                Camera.onPreCull += OnCameraPreCull;
            }
            catch (Exception) { }

            Logger.LogInfo($"{PluginName} v{Version} loaded, bone: {BoneName.Value}, mode: {LockMode.Value}, enabled: {Enabled.Value}");
        }

        /// <summary>
        /// POV 激活时隔离 Ermin 的摇杆移动/转向(它的右摇杆上下被识别为 Y 轴高度移动,
        /// 与第一人称的俯仰转头冲突,导致头显绕圈/飞裤裆)。POV 关闭时恢复 Ermin 行为。
        /// 手动 patch(类型 internal,不能用特性 PatchAll,失败也不影响其他 patch)。
        /// </summary>
        private static void PatchErminThumbstickIsolation(Harmony harmony)
        {
            try
            {
                var erType = AccessTools.TypeByName("KKCharaStudioVR.GripMoveKKCharaStudioTool");
                if (erType == null)
                {
                    // Ermin 的 DLL 在 BepInEx 根目录,可能未被自动加载:手动加载后再查
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(
                            System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "KKCharaStudioVRPlugin.dll"));
                        erType = asm.GetType("KKCharaStudioVR.GripMoveKKCharaStudioTool");
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning("POVIsolate: load Ermin dll failed " + e.Message);
                    }
                }
                if (erType == null)
                {
                    Log.LogWarning("POVIsolate: Ermin tool type not found, skip");
                    return;
                }
                var m = AccessTools.Method(erType, "HandleThumbstickLocomotion");
                if (m == null)
                {
                    Log.LogWarning("POVIsolate: HandleThumbstickLocomotion not found, skip");
                    return;
                }
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(KKPEHeightLockPlugin).GetMethod(
                    "ErminThumbstickPrefix", BindingFlags.NonPublic | BindingFlags.Static)));
                Log.LogInfo("POVIsolate: Ermin thumbstick locomotion isolated (POV 时禁用)");
            }
            catch (Exception e)
            {
                Log.LogWarning("POVIsolate: patch failed " + e.Message);
            }
        }

        /// <summary>POV 激活时跳过 Ermin 摇杆移动/转向的 Prefix。</summary>
        private static bool ErminThumbstickPrefix()
        {
            return !POVEnabled.Value;
        }

        private void Start()
        {
            StudioSaveLoadApi.SceneLoad += OnSceneLoad;
            ConfigPanel.Init(this);
        }

        private void OnDestroy()
        {
            StudioSaveLoadApi.SceneLoad -= OnSceneLoad;
            ConfigPanel.Dispose();
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                Enabled.Value = !Enabled.Value;
                Logger.LogMessage($"{PluginName}: {(Enabled.Value ? "enabled" : "disabled")}");
            }

            if (ReplaceKey.Value.IsDown())
            {
                Logger.LogMessage($"{PluginName}: manual character replace triggered");
                SceneReplacer.ReplaceAllCharacters();
            }

            if (POVToggleKey.Value.IsDown())
            {
                POVEnabled.Value = !POVEnabled.Value;
                Logger.LogMessage($"{PluginName}: first-person POV {(POVEnabled.Value ? "enabled" : "disabled")}");
            }

            // 驱动第一人称视角(VR:左手 Y 键切换 + 右手摇杆转头 + 头部跟随)
            VRFirstPersonPOV.UpdatePOV(POVEnabled.Value);

            // 驱动 VR 悬浮菜单(面板跟随 + 右手射线交互;左手 Y 键开合见 VRFirstPersonPOV.CheckLeftYButtonToggle)
            VRFloatingPanel.UpdatePanel();
        }

        /// <summary>渲染前最后一刻钉住第一人称 rig(此时 SteamVR 追踪与各插件移动都已执行完)。</summary>
        private static void OnCameraPreCull(Camera cam)
        {
            try
            {
                VRFirstPersonPOV.LateUpdatePOV();
            }
            catch (Exception) { }
        }

        private void LateUpdate()
        {
            // SteamVR 追踪更新头显 pose 后,再次钉住第一人称 rig(俯仰/偏航都不移动摄像机位置)
            VRFirstPersonPOV.LateUpdatePOV();
        }

        private void OnGUI()
        {
            ConfigPanel.OnGUI();
        }

        private void OnSceneLoad(object sender, SceneLoadEventArgs args)
        {
            if (!Enabled.Value) return;
            if (args.Operation != SceneOperationKind.Load) return;

            // 场景工具:去无用球体 / 去码(独立开关,与角色替换互不影响)
            if (AutoRemoveSpheres.Value)
                SceneTools.RemoveUselessSpheres();
            if (AutoDecensor.Value)
                SceneTools.DecensorScene();

            // 刷新场景播放器列表(更新当前场景索引)
            ScenePlayerModule.RefreshSceneList();

            if (!AutoReplaceOnLoad.Value) return;

            // 场景加载后角色需要几帧才完全构建,延迟执行
            Logger.LogMessage($"{PluginName}: scene loaded, will replace characters by sex");
            SceneReplacer.ScheduleReplace(this, args.LoadedObjects);
        }
    }

    /// <summary>
    /// 自动把角色骨骼 cf_n_height 标记为 KKPE BonesEditor 的 dirty(scale),
    /// 使 KKPE 每帧强制写回该骨骼的缩放,从而锁定身高,不被动画/姿势覆盖。
    /// 仅在 LockMode == HeightOnly 时生效。
    /// </summary>
    [HarmonyPatch(typeof(BonesEditor), "ApplyBoneManualCorrection")]
    public static class HeightLockPatches
    {
        private static string BoneName => KKPEHeightLockPlugin.BoneName.Value;

        // 反射缓存
        private static FieldInfo _targetField;
        private static FieldInfo _dirtyBonesField;
        private static FieldInfo _typeField;
        private static FieldInfo _ociCharField;
        private static MethodInfo _setBoneScaleMethod;
        private static MethodInfo _dirtyBonesContainsKey;
        private static MethodInfo _dirtyBonesGetItem;
        private static FieldInfo _transformDataScaleField;
        private static PropertyInfo _editableValueValue;
        private static bool _reflectionReady;

        // 按角色缓存的骨骼查找(角色销毁后旧引用失效,会重新查找并清理)
        private static readonly System.Collections.Generic.Dictionary<Studio.OCIChar, Transform> _boneCache =
            new System.Collections.Generic.Dictionary<Studio.OCIChar, Transform>();

        // 每个角色的锁定基准缩放(首次 = 角色加载/首次见到时的值;用户在 KKPE 手动调整后同步)
        private static readonly System.Collections.Generic.Dictionary<Studio.OCIChar, Vector3> _lockedScales =
            new System.Collections.Generic.Dictionary<Studio.OCIChar, Vector3>();

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;

            var bonesEditorType = typeof(BonesEditor);
            _targetField = AccessTools.Field(bonesEditorType, "_target");
            _dirtyBonesField = AccessTools.Field(bonesEditorType, "_dirtyBones");
            _setBoneScaleMethod = AccessTools.Method(bonesEditorType, "SetBoneScale", new[] { typeof(Transform), typeof(Vector3) });

            var targetType = AccessTools.TypeByName("HSPE.AMModules.GenericOCITarget");
            if (targetType != null)
            {
                _typeField = AccessTools.Field(targetType, "type");
                _ociCharField = AccessTools.Field(targetType, "ociChar");
            }

            // _dirtyBones 是 Dictionary<GameObject, TransformData>,反射取 ContainsKey / Item
            if (_dirtyBonesField != null)
            {
                var dirtyType = _dirtyBonesField.FieldType;
                _dirtyBonesContainsKey = dirtyType.GetMethod("ContainsKey", new[] { typeof(GameObject) });
                _dirtyBonesGetItem = dirtyType.GetMethod("get_Item", new[] { typeof(GameObject) });
            }

            // TransformData.scale(EditableValue<Vector3>) 与 EditableValue.get_value
            var tdType = AccessTools.TypeByName("HSPE.AMModules.BonesEditor/TransformData");
            if (tdType != null)
            {
                _transformDataScaleField = tdType.GetField("scale");
                var evType = AccessTools.TypeByName("HSPE.EditableValue`1");
                if (evType != null)
                {
                    var evVector3 = evType.MakeGenericType(typeof(Vector3));
                    _editableValueValue = evVector3.GetProperty("value");
                }
            }

            _reflectionReady = true;
            if (_targetField == null || _dirtyBonesField == null || _setBoneScaleMethod == null ||
                _typeField == null || _ociCharField == null || _dirtyBonesContainsKey == null ||
                _dirtyBonesGetItem == null || _transformDataScaleField == null || _editableValueValue == null)
                KKPEHeightLockPlugin.Log.LogError("KKPEHeightLock: reflection setup incomplete, plugin disabled. targetField=" +
                    (_targetField != null) + " dirtyBonesField=" + (_dirtyBonesField != null) +
                    " setBoneScale=" + (_setBoneScaleMethod != null) + " typeField=" + (_typeField != null) +
                    " ociCharField=" + (_ociCharField != null) + " containsKey=" + (_dirtyBonesContainsKey != null) +
                    " getItem=" + (_dirtyBonesGetItem != null) + " tdScale=" + (_transformDataScaleField != null) +
                    " evValue=" + (_editableValueValue != null));
        }

        private static void Prefix(BonesEditor __instance)
        {
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                if (KKPEHeightLockPlugin.LockMode.Value != BodyLockMode.HeightOnly) return;
                EnsureReflection();
                if (__instance == null || _targetField == null || _setBoneScaleMethod == null) return;

                var target = _targetField.GetValue(__instance);
                if (target == null || _typeField == null || _ociCharField == null) return;

                // 只处理角色(GenericOCITarget.Type.Character = 1)
                var typeVal = Convert.ToInt32(_typeField.GetValue(target));
                if (typeVal != 1) return;

                var ociChar = _ociCharField.GetValue(target) as Studio.OCIChar;
                if (ociChar == null || ociChar.charInfo == null) return;

                var bone = FindHeightBone(ociChar);
                if (bone == null) return;

                var dirtyBones = _dirtyBonesField.GetValue(__instance);
                var isDirty = (bool)_dirtyBonesContainsKey.Invoke(dirtyBones, new object[] { bone.gameObject });

                if (isDirty)
                {
                    // 已在 dirty 列表 → KKPE 每帧强制写回记录值,已锁定。
                    // 若用户手动调整过(KKPE 会更新 TransformData.scale),同步锁定基准。
                    var td = _dirtyBonesGetItem.Invoke(dirtyBones, new object[] { bone.gameObject });
                    if (td != null)
                    {
                        var ev = _transformDataScaleField.GetValue(td);
                        if (ev != null && (bool)ev.GetType().GetProperty("hasValue").GetValue(ev, null))
                            _lockedScales[ociChar] = (Vector3)_editableValueValue.GetValue(ev, null);
                    }
                    return;
                }

                // 不在 dirty → 用基准值(而非被动画污染的当前值)自动锁定 scale
                Vector3 lockedScale;
                if (!_lockedScales.TryGetValue(ociChar, out lockedScale))
                    lockedScale = bone.localScale; // 首次:记录当前缩放作为基准

                _setBoneScaleMethod.Invoke(__instance, new object[] { bone, lockedScale });
                _lockedScales[ociChar] = lockedScale;
                KKPEHeightLockPlugin.Log.LogDebug($"Locked scale of {bone.name} = {lockedScale}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock patch error: " + e);
            }
        }

        private static Transform FindHeightBone(Studio.OCIChar ociChar)
        {
            // 清理已销毁角色的缓存条目
            var staleKeys = null as System.Collections.Generic.List<Studio.OCIChar>;
            foreach (var kv in _boneCache)
            {
                if (kv.Key == null || kv.Value == null)
                {
                    if (staleKeys == null)
                        staleKeys = new System.Collections.Generic.List<Studio.OCIChar>();
                    staleKeys.Add(kv.Key);
                }
            }
            if (staleKeys != null)
                foreach (var k in staleKeys)
                {
                    _boneCache.Remove(k);
                    _lockedScales.Remove(k);
                }

            // 缓存命中且仍有效
            Transform cached;
            if (_boneCache.TryGetValue(ociChar, out cached) && cached != null)
                return cached;

            var root = ociChar.charInfo.transform;
            if (root == null) return null;

            var bone = FindChildRecursive(root, BoneName);
            if (bone != null)
                _boneCache[ociChar] = bone;
            return bone;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindChildRecursive(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }

    /// <summary>
    /// 变更角色(OCIChar.ChangeChara)时保留身材:
    /// ShapeOnly = 只写回体型滑块 shapeValueBody
    /// AllBody = 写回 shapeValueBody + bustSoftness/bustWeight + typeBone
    /// 替换后刷新模型身材与胸部物理。
    /// </summary>
    [HarmonyPatch(typeof(Studio.OCIChar), "ChangeChara")]
    public static class BodyPreservePatches
    {
        // 反射缓存
        private static PropertyInfo _shapeValueBodyProp;
        private static PropertyInfo _bustSoftnessProp;
        private static PropertyInfo _bustWeightProp;
        private static PropertyInfo _typeBoneProp;
        private static MethodInfo _updateShapeMethod;
        private static MethodInfo _updateBustMethod;
        private static bool _reflectionReady;

        // 保存替换前的身材(深拷贝)
        private static float[] _savedShapeValues;
        private static float _savedBustSoftness;
        private static float _savedBustWeight;
        private static int _savedTypeBone;

        // 保存替换前的姿势(骨骼名 → 局部变换)
        private static System.Collections.Generic.Dictionary<string, PoseData> _savedPose;

        // 保存替换前的服装(字节序列化)
        private static byte[] _savedCoordinateBytes;

        private class PoseData
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;
            _reflectionReady = true;

            var bodyType = AccessTools.TypeByName("ChaFileBody");
            if (bodyType != null)
            {
                _shapeValueBodyProp = bodyType.GetProperty("shapeValueBody");
                _bustSoftnessProp = bodyType.GetProperty("bustSoftness");
                _bustWeightProp = bodyType.GetProperty("bustWeight");
                _typeBoneProp = bodyType.GetProperty("typeBone");
            }

            var chaControlType = AccessTools.TypeByName("ChaControl");
            if (chaControlType != null)
            {
                _updateShapeMethod = AccessTools.Method(chaControlType, "UpdateShapeBodyValueFromCustomInfo");
                _updateBustMethod = AccessTools.Method(chaControlType, "UpdateBustSoftnessAndGravity");
            }

            if (_shapeValueBodyProp == null || _updateShapeMethod == null)
                KKPEHeightLockPlugin.Log.LogError("KKPEHeightLock: BodyPreserve reflection incomplete. shapeValueBody=" +
                    (_shapeValueBodyProp != null) + " bustSoftness=" + (_bustSoftnessProp != null) +
                    " bustWeight=" + (_bustWeightProp != null) + " typeBone=" + (_typeBoneProp != null) +
                    " updateShape=" + (_updateShapeMethod != null) + " updateBust=" + (_updateBustMethod != null));
        }

        private static void Prefix(Studio.OCIChar __instance)
        {
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                var mode = KKPEHeightLockPlugin.LockMode.Value;

                // 防御:清空上一次可能残留的状态(若上次 ChangeChara 中途异常,Postfix 未清)
                _savedShapeValues = null;
                _savedPose = null;
                _savedCoordinateBytes = null;

                // 姿势保留(独立于身材模式)
                if (KKPEHeightLockPlugin.PreservePoseOnReplace.Value)
                    SavePose(__instance);

                // 服装保留(独立于身材模式)
                if (KKPEHeightLockPlugin.PreserveClothesOnReplace.Value)
                    SaveCoordinate(__instance);

                if (mode == BodyLockMode.HeightOnly) return;

                EnsureReflection();
                if (__instance == null || __instance.charInfo == null || _shapeValueBodyProp == null) return;

                var body = __instance.charInfo.fileBody;
                if (body == null) return;

                // 深拷贝体型滑块数组(新卡会替换整个数组,必须复制)
                var arr = _shapeValueBodyProp.GetValue(body, null) as float[];
                _savedShapeValues = arr != null ? (float[])arr.Clone() : null;

                if (mode == BodyLockMode.AllBody && _bustSoftnessProp != null && _bustWeightProp != null && _typeBoneProp != null)
                {
                    _savedBustSoftness = (float)_bustSoftnessProp.GetValue(body, null);
                    _savedBustWeight = (float)_bustWeightProp.GetValue(body, null);
                    _savedTypeBone = (int)_typeBoneProp.GetValue(body, null);
                }
                KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved {(_savedShapeValues != null ? _savedShapeValues.Length : 0)} shape values, mode={mode}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock BodyPreserve prefix error: " + e);
            }
        }

        /// <summary>保存角色所有 FK 骨骼的局部变换(姿势快照)。</summary>
        private static void SavePose(Studio.OCIChar ociChar)
        {
            _savedPose = new System.Collections.Generic.Dictionary<string, PoseData>();
            if (ociChar == null || ociChar.listBones == null) return;

            foreach (var boneInfo in ociChar.listBones)
            {
                try
                {
                    if (boneInfo == null) continue;
                    var go = boneInfo.gameObject;
                    if (go == null) continue;
                    var t = go.transform;
                    if (t == null) continue;
                    _savedPose[go.name] = new PoseData
                    {
                        localPosition = t.localPosition,
                        localRotation = t.localRotation,
                        localScale = t.localScale
                    };
                }
                catch (Exception) { }
            }
            KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved {_savedPose.Count} bone transforms (pose)");
        }

        /// <summary>保存当前服装(坐标),用字节序列化深拷贝。</summary>
        private static void SaveCoordinate(Studio.OCIChar ociChar)
        {
            _savedCoordinateBytes = null;
            if (ociChar == null || ociChar.charInfo == null) return;
            var coord = ociChar.charInfo.nowCoordinate;
            if (coord != null)
            {
                try
                {
                    _savedCoordinateBytes = coord.SaveBytes();
                    KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved coordinate bytes ({_savedCoordinateBytes.Length})");
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: failed to save coordinate: " + e.Message);
                }
            }
        }

        private static void Postfix(Studio.OCIChar __instance)
        {
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                var mode = KKPEHeightLockPlugin.LockMode.Value;

                // 恢复服装(独立于身材模式,替换后立即恢复)
                if (KKPEHeightLockPlugin.PreserveClothesOnReplace.Value && _savedCoordinateBytes != null)
                    RestoreCoordinate(__instance);

                // 恢复姿势:骨架刚重建,延迟几帧再写回
                if (KKPEHeightLockPlugin.PreservePoseOnReplace.Value && _savedPose != null)
                {
                    var pose = _savedPose;
                    _savedPose = null;
                    SchedulePoseRestore(__instance, pose);
                }

                if (mode == BodyLockMode.HeightOnly || _savedShapeValues == null) return;

                EnsureReflection();
                if (__instance == null || __instance.charInfo == null) return;

                var body = __instance.charInfo.fileBody;
                if (body == null) return;

                // 写回体型滑块
                _shapeValueBodyProp.SetValue(body, _savedShapeValues, null);

                if (mode == BodyLockMode.AllBody && _bustSoftnessProp != null && _bustWeightProp != null && _typeBoneProp != null)
                {
                    _bustSoftnessProp.SetValue(body, _savedBustSoftness, null);
                    _bustWeightProp.SetValue(body, _savedBustWeight, null);
                    _typeBoneProp.SetValue(body, _savedTypeBone, null);
                }

                // 刷新模型身材
                if (_updateShapeMethod != null)
                    _updateShapeMethod.Invoke(__instance.charInfo, null);
                if (mode == BodyLockMode.AllBody && _updateBustMethod != null)
                    _updateBustMethod.Invoke(__instance.charInfo, null);

                KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: restored shape values (mode={mode})");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock BodyPreserve postfix error: " + e);
            }
            finally
            {
                // 无论成功失败,清空保存状态,避免误应用到下一次替换
                _savedShapeValues = null;
                _savedCoordinateBytes = null;
            }
        }

        /// <summary>恢复服装(坐标):把保存的字节数据赋给新角色。</summary>
        private static void RestoreCoordinate(Studio.OCIChar ociChar)
        {
            try
            {
                if (ociChar == null || ociChar.charInfo == null || _savedCoordinateBytes == null) return;
                var newCoord = new ChaFileCoordinate();
                if (newCoord.LoadBytes(_savedCoordinateBytes, ChaFileDefine.ChaFileCoordinateVersion))
                {
                    ociChar.charInfo.SetNowCoordinate(newCoord);
                    KKPEHeightLockPlugin.Log.LogDebug("BodyPreserve: restored clothes");
                }
                else
                {
                    KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: LoadBytes failed, clothes not restored");
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: restore coordinate error: " + e.Message);
            }
        }

        /// <summary>延迟恢复姿势:通过协程等几帧,让新骨架完全构建后写回骨骼变换。
        /// pose 字典作为参数传入,避免多角色同帧替换时静态状态串扰。</summary>
        private static void SchedulePoseRestore(Studio.OCIChar ociChar, System.Collections.Generic.Dictionary<string, PoseData> pose)
        {
            try
            {
                var host = KKPEHeightLockPlugin.Instance;
                if (host == null) return;
                host.StartCoroutine(PoseRestoreRoutine(ociChar, pose));
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: schedule pose restore error: " + e.Message);
            }
        }

        private static System.Collections.IEnumerator PoseRestoreRoutine(Studio.OCIChar ociChar, System.Collections.Generic.Dictionary<string, PoseData> pose)
        {
            // 等 5 帧,让 ChangeChara 的骨架重建和 FK 初始化完成
            for (int i = 0; i < 5; i++)
                yield return null;

            if (pose == null || ociChar == null || ociChar.listBones == null) yield break;

            int restored = 0;
            foreach (var boneInfo in ociChar.listBones)
            {
                try
                {
                    if (boneInfo == null) continue;
                    var go = boneInfo.gameObject;
                    if (go == null) continue;
                    PoseData data;
                    if (!pose.TryGetValue(go.name, out data)) continue;
                    var t = go.transform;
                    if (t == null) continue;
                    t.localPosition = data.localPosition;
                    t.localRotation = data.localRotation;
                    t.localScale = data.localScale;
                    restored++;
                }
                catch (Exception) { }
            }
            KKPEHeightLockPlugin.Log.LogMessage($"BodyPreserve: restored {restored} bone transforms (pose)");
        }
    }

    /// <summary>
    /// 场景加载后按性别自动/手动替换角色。
    /// 自动模式:由 KKAPI 的 SceneLoad 事件触发,延迟数帧等角色完全构建后执行 ChangeChara。
    /// 手动模式:按快捷键或调用 ReplaceAllCharacters 立即替换当前场景角色。
    /// </summary>
    public static class SceneReplacer
    {
        private static MonoBehaviour _coroutineHost;
        private static bool _scheduled;
        private static System.Collections.Generic.List<Studio.OCIChar> _pendingCharacters;

        public static void ScheduleReplace(MonoBehaviour host, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, Studio.ObjectCtrlInfo>> loadedObjects)
        {
            if (_scheduled) return;
            _scheduled = true;
            _coroutineHost = host;
            _pendingCharacters = CollectCharacters(loadedObjects);
            if (_coroutineHost != null)
                _coroutineHost.StartCoroutine(ReplaceRoutine());
        }

        private static System.Collections.IEnumerator ReplaceRoutine()
        {
            // 等 30 帧,确保场景角色完全加载构建
            for (int i = 0; i < 30; i++)
                yield return null;

            _scheduled = false;
            var chars = _pendingCharacters;
            _pendingCharacters = null;
            ReplaceCharacters(chars);
        }

        public static void ReplaceAllCharacters()
        {
            if (!KKPEHeightLockPlugin.Enabled.Value) return;
            ReplaceCharacters(GetSceneCharacters());
        }

        private static void ReplaceCharacters(System.Collections.Generic.List<Studio.OCIChar> characters)
        {
            try
            {
                var femalePath = KKPEHeightLockPlugin.FemaleCardPath.Value;
                var malePath = KKPEHeightLockPlugin.MaleCardPath.Value;
                if (string.IsNullOrEmpty(femalePath) && string.IsNullOrEmpty(malePath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer: no card paths configured, nothing to replace");
                    return;
                }

                int replaced = 0;
                if (characters != null)
                {
                    foreach (var oci in characters)
                    {
                        if (oci == null || oci.charInfo == null) continue;

                        string path = null;
                        try
                        {
                            var sex = oci.sex;
                            if (sex == 1 && !string.IsNullOrEmpty(femalePath) && KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value)
                                path = femalePath;
                            else if (sex == 0 && !string.IsNullOrEmpty(malePath) && KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value)
                                path = malePath;
                        }
                        catch (Exception e)
                        {
                            KKPEHeightLockPlugin.Log.LogWarning($"SceneReplacer: failed to read sex: {e.Message}");
                            continue;
                        }

                        if (path == null) continue;

                        KKPEHeightLockPlugin.Log.LogMessage($"SceneReplacer: replacing {(oci.sex == 1 ? "female" : "male")} character with {path}");
                        oci.ChangeChara(path);
                        replaced++;
                    }
                }

                KKPEHeightLockPlugin.Log.LogMessage($"SceneReplacer: replaced {replaced} character(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer error: " + e);
            }
        }

        private static System.Collections.Generic.List<Studio.OCIChar> CollectCharacters(
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, Studio.ObjectCtrlInfo>> loadedObjects)
        {
            var result = new System.Collections.Generic.List<Studio.OCIChar>();
            if (loadedObjects == null) return result;
            foreach (var kv in loadedObjects)
            {
                var oci = kv.Value as Studio.OCIChar;
                if (oci != null)
                    result.Add(oci);
            }
            return result;
        }

        public static System.Collections.Generic.List<Studio.OCIChar> GetSceneCharacters()
        {
            var result = new System.Collections.Generic.List<Studio.OCIChar>();
            try
            {
                // 遍历当前场景所有对象(Studio.Studio.Instance.dicObjectCtrl 含全部对象)
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return result;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oci = kv.Value as Studio.OCIChar;
                    if (oci != null)
                        result.Add(oci);
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer: GetSceneCharacters error: " + e);
            }
            return result;
        }
    }

    /// <summary>
    /// 左侧工具栏按钮(类似 KKPE/VNGE 的小方块)打开配置窗口:
    /// 选择女/男替换角色卡(工作室原生文件选择器)、开关、立即替换。
    /// </summary>
    public static class ConfigPanel
    {
        private const int WindowId = 891273;
        private const int CardPickWindowId = 891274;
        private const int SubdirPickWindowId = 891275;
        private static BaseUnityPlugin _plugin;
        private static bool _windowVisible;
        private static Rect _windowRect = new Rect(200, 100, 560, 660);
        private static Vector2 _windowScrollPos;
        private static bool _clothesPartsExpanded;
        private static float _windowContentHeight;
        private static KKAPI.Studio.UI.Toolbars.ToolbarControlBase _toolbarButton;

        // 子目录选择窗口状态(通用:场景/随机女/随机男)
        private static bool _subdirPickerVisible;
        private static int _subdirPickerMode; // 0=场景 1=随机女 2=随机男
        private static Rect _subdirPickerRect = new Rect(200, 120, 480, 420);
        private static Vector2 _subdirScroll;
        private static string _subdirPickerPath = ""; // 当前浏览的路径
        private static System.Collections.Generic.List<string> _subdirList = new System.Collections.Generic.List<string>();

        // 游戏内选卡窗口状态
        private static bool _cardPickerVisible;
        private static bool _cardPickerIsFemale;
        private static Rect _cardPickerRect = new Rect(150, 80, 640, 500);
        private static Vector2 _cardScroll;
        private static string _cardSearch = "";
        private static string _cardPickerCurrentDir = ""; // 相对目录,"" = 根目录
        private static System.Collections.Generic.List<CardEntry> _cardEntries;
        private static System.Collections.Generic.List<string> _cardSubFolders;
        private static int _cardsProcessedThisFrame;

        // 配置窗口背景图(外置文件,可替换)
        private static Texture2D _windowBackground;
        private static bool _backgroundAttempted;
        private static bool _resizingWindow;
        private static bool _draggingWindow;
        private static Vector2 _dragStartMouse;
        private static Vector2 _dragStartWinPos;

        private class CardEntry
        {
            public string path;
            public string name;     // 角色名(懒加载,初始为 null)
            public Texture2D texture; // 缩略图(懒加载)
            public bool metaLoaded; // 角色名是否已加载
        }

        public static void Init(BaseUnityPlugin plugin)
        {
            _plugin = plugin;
            try
            {
                var icon = CreateIcon();
                var btn = new KKAPI.Studio.UI.Toolbars.SimpleToolbarButton(
                    "KKPEHeightLock.Config",
                    "场景卡套档工具箱 设置",
                    () => icon,
                    plugin,
                    _ => { _windowVisible = !_windowVisible; });

                if (KKAPI.Studio.UI.Toolbars.ToolbarManager.AddLeftToolbarControl(btn))
                {
                    _toolbarButton = btn;
                    KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: toolbar button created");
                }
                else
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: toolbar button registration failed");
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to create toolbar button: " + e);
            }
        }

        public static void Dispose()
        {
            if (_toolbarButton != null)
            {
                try { _toolbarButton.Dispose(); } catch { }
                _toolbarButton = null;
            }
        }

        /// <summary>加载工具栏图标:优先使用 BepInEx/plugins/KKPEHeightLock/icon.png(用户可替换),
        /// 找不到或加载失败则回退到程序化生成的蓝色箭头。</summary>
        private static Texture2D CreateIcon()
        {
            // 1) 尝试加载外部图标文件(自动缩放到 32x32)
            try
            {
                var iconPath = System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "KKPEHeightLock"), "icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    var tex = PngAssist.LoadTexture(iconPath);
                    if (tex != null)
                    {
                        if (tex.width == 32 && tex.height == 32)
                        {
                            KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: using custom icon (32x32)");
                            return tex;
                        }
                        var scaled = ScaleTexture(tex, 32, 32);
                        KKPEHeightLockPlugin.Log.LogInfo($"KKPEHeightLock: using custom icon scaled {tex.width}x{tex.height} -> 32x32");
                        return scaled;
                    }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to load custom icon, using default: " + e.Message);
            }

            // 2) 回退:程序化生成蓝色方块 + 白色箭头
            const int size = 32;
            var fallback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = new Color(0.15f, 0.4f, 0.85f, 1f); // 蓝色底
                    // 画一个白色箭头(↑),适配 32x32
                    bool arrow = (x >= size / 2 - 3 && x <= size / 2 + 3 && y >= size / 2 - 5 && y <= size / 2 + 7) ||
                                 (y >= size / 2 - 5 && y <= size / 2 - 3 && x >= size / 2 - 8 && x <= size / 2 + 8);
                    if (arrow) c = Color.white;
                    fallback.SetPixel(x, y, c);
                }
            }
            fallback.Apply();
            return fallback;
        }

        /// <summary>高质量缩放 Texture2D(Catmull-Rom 双三次插值,比双线性清晰很多)。</summary>
        private static Texture2D ScaleTexture(Texture2D source, int newWidth, int newHeight)
        {
            var result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            int srcW = source.width;
            int srcH = source.height;
            float scaleX = (float)srcW / newWidth;
            float scaleY = (float)srcH / newHeight;

            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    // 映射到源图坐标(像素中心)
                    float fx = (x + 0.5f) * scaleX - 0.5f;
                    float fy = (y + 0.5f) * scaleY - 0.5f;
                    result.SetPixel(x, y, SampleCatmullRom(source, fx, fy, srcW, srcH));
                }
            }
            result.Apply();
            return result;
        }

        /// <summary>Catmull-Rom 双三次采样(4x4 邻域加权)。</summary>
        private static Color SampleCatmullRom(Texture2D tex, float fx, float fy, int srcW, int srcH)
        {
            int x0 = (int)Mathf.Floor(fx);
            int y0 = (int)Mathf.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            // 计算 4x4 权重
            float[] wx = CatmullRomWeights(tx);
            float[] wy = CatmullRomWeights(ty);

            Color result = new Color(0f, 0f, 0f, 0f);
            for (int j = 0; j < 4; j++)
            {
                int sy = Mathf.Clamp(y0 - 1 + j, 0, srcH - 1);
                for (int i = 0; i < 4; i++)
                {
                    int sx = Mathf.Clamp(x0 - 1 + i, 0, srcW - 1);
                    float w = wx[i] * wy[j];
                    if (w != 0f)
                        result += tex.GetPixel(sx, sy) * w;
                }
            }
            return result;
        }

        private static float[] CatmullRomWeights(float t)
        {
            // Catmull-Rom 基函数(标准 4 点形式)
            float t2 = t * t;
            float t3 = t2 * t;
            return new float[]
            {
                0.5f * (-t3 + 2f * t2 - t),        // w(-1)
                0.5f * (3f * t3 - 5f * t2 + 2f),   // w(0)
                0.5f * (-3f * t3 + 4f * t2 + t),   // w(1)
                0.5f * (t3 - t2)                    // w(2)
            };
        }

        internal static void OnGUI()
        {
            // 整体缩放(VR 用):矩阵缩放后,所有控件/文字等比放大
            float scale = Mathf.Max(0.5f, KKPEHeightLockPlugin.UIScale.Value);
            var oldMatrix = GUI.matrix;
            if (scale != 1f)
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // 完全自研窗口交互:不用 GUI.Window 返回值(会与我们的修改冲突)。
            // 拖动/resize 都在窗口函数内用绝对坐标处理。
            if (_windowVisible)
                GUI.Window(WindowId, _windowRect, DrawWindow, "场景卡套档工具箱");
            if (_cardPickerVisible)
                GUI.Window(CardPickWindowId, _cardPickerRect, DrawCardPicker, "选择角色卡");
            if (_subdirPickerVisible)
                GUI.Window(SubdirPickWindowId, _subdirPickerRect, DrawSubdirPicker, "选择子目录");

            GUI.matrix = oldMatrix;
        }

        /// <summary>呼出/关闭腕表菜单(配置窗口)。</summary>
        internal static void ToggleWristMenu()
        {
            _windowVisible = !_windowVisible;
            KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: menu {( _windowVisible ? "opened" : "closed")}");
        }

        /// <summary>判断是否处于 VR 模式(SteamVR 相机存在或 Ermin VR 插件活跃)。</summary>
        private static bool IsVRMode()
        {
            try
            {
                var pc = UnityEngine.Object.FindObjectOfType<VRGIN.Visuals.PlayerCamera>();
                if (pc != null) return true;
            }
            catch (Exception) { }
            return false;
        }

        private static void DrawWindow(int id)
        {
            // 背景图(如有)
            DrawWindowBackground();

            // 增强皮肤:深色背景上按钮/文字更醒目
            GUISkin oldSkin = GUI.skin;
            GUI.skin = BuildVRSkin(oldSkin);

            // 右上角关闭小按钮(绝对定位)
            var closeRect = new Rect(_windowRect.width - 30, 4, 24, 22);
            if (GUI.Button(closeRect, "×"))
                _windowVisible = false;

            // 内容可上下滚动(垂直滚动条),禁止水平滚动条(左右不滑)
            // 内容可上下滚动;滚动视图宽度=内容区宽度(内容本身不超宽,绝不出现水平滚动条)
            _windowScrollPos = GUILayout.BeginScrollView(_windowScrollPos, false, true, GUILayout.ExpandWidth(true), GUILayout.Height(_windowRect.height - 24));
            GUILayout.BeginVertical();

            // --- 免责声明 ---
            var oldStyle = GUI.skin.label;
            var warnStyle = new GUIStyle(GUI.skin.label);
            warnStyle.normal.textColor = Color.yellow;
            warnStyle.fontStyle = FontStyle.Bold;
            warnStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("该插件为完全免费发布,倒狗和贩子全家死光。", warnStyle);
            GUI.skin.label = oldStyle;
            GUILayout.Space(4);

            // --- 场景播放器(ScenePlayer 复刻,菜单最顶端) ---
            DrawSectionBox("场景播放器", new Color(0.30f, 0.20f, 0.60f, 0.8f), new Color(0.55f, 0.30f, 0.80f, 0.95f));

            // 场景浏览行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.LoadPrevScene();
            if (GUILayout.Button("刷新", GUILayout.Width(52), GUILayout.Height(32)))
                ScenePlayerModule.RefreshSceneList();
            GUILayout.Label(ScenePlayerModule.SceneLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(32));
            if (GUILayout.Button("▶", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.LoadNextScene();
            GUILayout.EndHorizontal();

            // 场景子目录(点"选择"弹出子目录窗口)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择子目录", GUILayout.Height(28)))
                OpenSubdirPicker(0);
            GUILayout.Label("场景: " + ScenePlayerModule.SceneSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(300), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("删除", GUILayout.Width(52), GUILayout.Height(30)))
                ScenePlayerModule.DeleteCurrentScene(true);
            GUILayout.Label("场景播放器:上/下一场景,加载场景", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic }, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // 随机替换角色行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("随机女", GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceFemale();
            if (GUILayout.Button("随机男", GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceMale();
            if (GUILayout.Button("随机全部", GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceAll();
            GUILayout.EndHorizontal();

            // 随机子目录(点"选择"弹出子目录窗口)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("女卡目录", GUILayout.Height(26)))
                OpenSubdirPicker(1);
            GUILayout.Label(ScenePlayerModule.RandomFemaleSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(26));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("男卡目录", GUILayout.Height(26)))
                OpenSubdirPicker(2);
            GUILayout.Label(ScenePlayerModule.RandomMaleSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(26));
            GUILayout.EndHorizontal();

            // FSS 子场景行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("FSS", GUILayout.Width(44), GUILayout.Height(30)))
                ScenePlayerModule.RefreshFSS();
            if (GUILayout.Button("◀", GUILayout.Width(32), GUILayout.Height(30)))
                ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex - 1);
            GUILayout.Label(ScenePlayerModule.FssLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(30));
            if (GUILayout.Button("▶", GUILayout.Width(32), GUILayout.Height(30)))
                ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex + 1);
            GUILayout.EndHorizontal();

            // Timeline 控制行
            GUILayout.BeginHorizontal();
            bool tlPlaying = ScenePlayerModule.TimelinePlaying;
            if (GUILayout.Button(tlPlaying ? "⏸" : "▶", GUILayout.Width(36), GUILayout.Height(32)))
            {
                if (tlPlaying) ScenePlayerModule.TimelinePause();
                else ScenePlayerModule.TimelinePlay();
            }
            if (GUILayout.Button("⏹", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.TimelineStop();
            // CAM 按钮:Timeline 有相机关键帧时显示,切换相机是否受控
            if (ScenePlayerModule.TimelineHasCameraKeyframe)
            {
                if (GUILayout.Button(ScenePlayerModule.TimelineCameraControlled ? "CAM●" : "CAM○", GUILayout.Width(52), GUILayout.Height(32)))
                    ScenePlayerModule.TimelineToggleCamera();
            }
            GUILayout.Label(ScenePlayerModule.TimelineLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(32));
            GUILayout.EndHorizontal();

            // 服装:预设套装(校服1/校服2/体操/泳装/社团/便服/睡衣),当前选中高亮
            DrawSectionBox("服装预设", new Color(0.60f, 0.25f, 0.35f, 0.8f), new Color(0.85f, 0.40f, 0.50f, 0.95f));
            var editChara = ScenePlayerModule.CurrentEditChar();
            int curCoord = ScenePlayerModule.GetCoordinateType(editChara);
            string[] coordNames = { "校服1", "校服2", "体操", "泳装", "社团", "便服", "睡衣" };
            // 每行 4 个套装按钮(避免一行 7 个超宽出水平滚动条)
            const int coordPerRow = 4;
            for (int ci = 0; ci < coordNames.Length; ci++)
            {
                if (ci % coordPerRow == 0)
                    GUILayout.BeginHorizontal();
                bool isCur = (curCoord == ci);
                if (isCur)
                {
                    // 选中:绿色亮色高亮(原生按钮纹理上染绿)
                    var oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.15f, 0.75f, 0.30f, 1f);
                    if (GUILayout.Button("● " + coordNames[ci], GUILayout.Width(110), GUILayout.Height(30)))
                        ScenePlayerModule.SetCoordinateType(editChara, ci);
                    GUI.backgroundColor = oldBg;
                }
                else
                {
                    if (GUILayout.Button(coordNames[ci], GUILayout.Width(110), GUILayout.Height(30)))
                        ScenePlayerModule.SetCoordinateType(editChara, ci);
                }
                if (ci % coordPerRow == coordPerRow - 1 || ci == coordNames.Length - 1)
                    GUILayout.EndHorizontal();
            }

            // 部件三态(收纳,默认收起)
            _clothesPartsExpanded = GUILayout.Toggle(_clothesPartsExpanded, "部件三态(展开/收起)");
            if (_clothesPartsExpanded)
            {
                DrawClothesPartRow("上衣", 0);
                DrawClothesPartRow("下衣", 1);
                DrawClothesPartRow("内衣", 2);
                DrawClothesPartRow("内裤", 3);
                DrawClothesPartRow("袜", 10);
                DrawClothesPartRow("鞋", 11);
            }

            GUILayout.Space(4);

            // --- 场景替换设置 ---
            DrawSectionBox("场景替换角色", new Color(0.15f, 0.50f, 0.45f, 0.8f), new Color(0.30f, 0.75f, 0.60f, 0.95f));
            KKPEHeightLockPlugin.AutoReplaceOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoReplaceOnLoad.Value, "加载场景后自动替换");
            KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value, "替换女角色");
            KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value, "替换男角色");
            KKPEHeightLockPlugin.PreservePoseOnReplace.Value = GUILayout.Toggle(KKPEHeightLockPlugin.PreservePoseOnReplace.Value, "替换后保留原姿势");
            KKPEHeightLockPlugin.PreserveClothesOnReplace.Value = GUILayout.Toggle(KKPEHeightLockPlugin.PreserveClothesOnReplace.Value, "替换后保留原服装");

            GUILayout.BeginHorizontal();
            GUILayout.Label("女角色卡", GUILayout.Width(60), GUILayout.ExpandWidth(false));
            GUILayout.Label(FitPath(KKPEHeightLockPlugin.FemaleCardPath.Value), GUI.skin.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("选择", GUILayout.Width(60), GUILayout.ExpandWidth(false)))
                PickCard(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("男角色卡", GUILayout.Width(60), GUILayout.ExpandWidth(false));
            GUILayout.Label(FitPath(KKPEHeightLockPlugin.MaleCardPath.Value), GUI.skin.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("选择", GUILayout.Width(60), GUILayout.ExpandWidth(false)))
                PickCard(false);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("立即替换场景角色"))
                SceneReplacer.ReplaceAllCharacters();

            GUILayout.Space(4);

            // --- 场景工具:去无用球体 / 去码(放大按钮 + 自动开关) ---
            DrawSectionBox("场景工具", new Color(0.75f, 0.45f, 0.15f, 0.8f), new Color(0.95f, 0.65f, 0.30f, 0.95f));
            if (GUILayout.Button("一键消除无用球体", GUILayout.Height(36)))
                SceneTools.RemoveUselessSpheres();
            if (GUILayout.Button("一键去马赛克", GUILayout.Height(36)))
                SceneTools.DecensorScene();
            KKPEHeightLockPlugin.AutoRemoveSpheres.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoRemoveSpheres.Value, "加载场景时自动去无用球体");
            KKPEHeightLockPlugin.AutoDecensor.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoDecensor.Value, "加载场景时自动去马赛克");

            GUILayout.Space(8);

            // --- 身高/身材设置 ---
            DrawSectionBox("身高 / 身材锁定", new Color(0.50f, 0.20f, 0.55f, 0.8f), new Color(0.75f, 0.40f, 0.80f, 0.95f));
            KKPEHeightLockPlugin.Enabled.Value = GUILayout.Toggle(KKPEHeightLockPlugin.Enabled.Value, "启用锁定");
            KKPEHeightLockPlugin.LockMode.Value = (BodyLockMode)GUILayout.SelectionGrid(
                (int)KKPEHeightLockPlugin.LockMode.Value,
                new[] { "仅身高骨骼", "仅体型滑块", "全部体型" },
                3);

            GUILayout.Space(8);

            // --- UI 缩放(VR) ---
            DrawSectionBox("界面缩放(VR)", new Color(0.25f, 0.35f, 0.55f, 0.8f), new Color(0.45f, 0.60f, 0.85f, 0.95f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("缩放:", GUILayout.Width(50));
            KKPEHeightLockPlugin.UIScale.Value = GUILayout.HorizontalSlider(KKPEHeightLockPlugin.UIScale.Value, 0.5f, 2.5f);
            GUILayout.Label($"{KKPEHeightLockPlugin.UIScale.Value:0.0}x", GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.Label("调整后界面立即生效", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.Space(8);

            // --- VR 第一人称 ---
            DrawSectionBox("VR 第一人称", new Color(0.20f, 0.40f, 0.70f, 0.8f), new Color(0.35f, 0.65f, 0.95f, 0.95f));
            if (GUILayout.Button(KKPEHeightLockPlugin.POVEnabled.Value ? "关闭VR第一人称" : "开启VR第一人称", GUILayout.Height(36)))
            {
                KKPEHeightLockPlugin.POVEnabled.Value = !KKPEHeightLockPlugin.POVEnabled.Value;
                KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: first-person POV {(KKPEHeightLockPlugin.POVEnabled.Value ? "enabled" : "disabled")}");
            }

            DrawStepRow("高低", KKPEHeightLockPlugin.POVHeightOffset, 0.05f, -1f, 1f, "0.00");
            DrawStepRow("左右", KKPEHeightLockPlugin.POVLateralOffset, 0.05f, -1f, 1f, "0.00");
            DrawStepRow("前后", KKPEHeightLockPlugin.POVViewOffset, 0.05f, -1f, 1f, "0.00");
            DrawStepRow("FOV", KKPEHeightLockPlugin.POVFOV, 5f, 30f, 150f, "0");
            DrawStepRow("转头速度", KKPEHeightLockPlugin.POVLookSpeed, 0.5f, 0.5f, 10f, "0.0");
            GUILayout.Label("VR 中:右手摇杆转头,推到底手柄朝向即视角。选中角色后开启。", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.Space(8);

            // --- 场景人物(链接工作室选中) ---
            DrawSectionBox("场景人物(点击选中)", new Color(0.20f, 0.60f, 0.55f, 0.8f), new Color(0.35f, 0.80f, 0.70f, 0.95f));
            var vrChars = ScenePlayerModule.GetSceneCharacters();
            if (vrChars.Count == 0)
            {
                GUILayout.Label("场景中没有角色。", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });
            }
            else
            {
                foreach (var c in vrChars)
                {
                    GUILayout.BeginHorizontal();
                    bool isSel = ScenePlayerModule.IsCharacterSelected(c);
                    if (GUILayout.Button(isSel ? "● 选中" : "○ 选中", GUILayout.Width(76), GUILayout.Height(30)))
                    {
                        ScenePlayerModule.SelectCharacter(c);
                        isSel = true;
                    }
                    string cname = "角色";
                    try
                    {
                        if (c.charInfo != null && c.charInfo.fileParam != null && !string.IsNullOrEmpty(c.charInfo.fileParam.fullname))
                            cname = c.charInfo.fileParam.fullname;
                    }
                    catch (Exception) { }
                    var nameStyle = new GUIStyle(GUI.skin.label);
                    if (isSel) { nameStyle.fontStyle = FontStyle.Bold; nameStyle.normal.textColor = new Color(0.4f, 1f, 0.7f); }
                    GUILayout.Label(cname, nameStyle, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("点击角色名左侧按钮,可在工作室中选中该角色(供第一人称/服装预设/替换使用)。", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            // 恢复原皮肤
            GUI.skin = oldSkin;

            // 自研窗口交互:拖动 + 右下角 resize,绝对坐标,稳定跟手
            HandleWindowInteraction();
        }

        /// <summary>窗口交互(拖动 + 右下角 resize)。
        /// 在窗口函数末尾调用:此时按钮/Toggle 已消费它们的 MouseDown(事件类型变 Used),
        /// 所以这里只会响应"空白处"的按下,不会误拖控件。
        /// 鼠标坐标(e.mousePosition)与 _windowRect 都是全局屏幕坐标,直接用绝对坐标计算。</summary>
        private static void HandleWindowInteraction()
        {
            var e = Event.current;
            if (e == null) return;

            const float handleSize = 22f;
            var handleRect = new Rect(
                _windowRect.x + _windowRect.width - handleSize,
                _windowRect.y + _windowRect.height - handleSize,
                handleSize, handleSize);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (handleRect.Contains(e.mousePosition))
                {
                    // 右下角:开始 resize,捕获鼠标(不 Use,保留事件给后续 MouseDrag)
                    _resizingWindow = true;
                    _draggingWindow = false;
                    _dragStartMouse = e.mousePosition;
                    _dragStartWinPos = new Vector2(_windowRect.width, _windowRect.height);
                    GUIUtility.hotControl = WindowId;
                }
                else if (_windowRect.Contains(e.mousePosition))
                {
                    // 窗口内空白处:开始拖动,捕获鼠标
                    _draggingWindow = true;
                    _resizingWindow = false;
                    _dragStartMouse = e.mousePosition;
                    _dragStartWinPos = new Vector2(_windowRect.x, _windowRect.y);
                    GUIUtility.hotControl = WindowId;
                }
            }
            else if (e.type == EventType.MouseDrag && (_draggingWindow || _resizingWindow))
            {
                if (GUIUtility.hotControl != WindowId) return;
                float scale = Mathf.Max(0.5f, KKPEHeightLockPlugin.UIScale.Value);
                if (_draggingWindow)
                {
                    // 绝对坐标移动:窗口左上 = 鼠标 - 按下时的偏移,完全跟手
                    _windowRect.x = _dragStartWinPos.x + (e.mousePosition.x - _dragStartMouse.x);
                    _windowRect.y = _dragStartWinPos.y + (e.mousePosition.y - _dragStartMouse.y);
                    // 防止完全拖出屏幕(至少保留标题栏在屏内);屏幕边界换算为逻辑坐标
                    _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 200f, Screen.width / scale - 60f);
                    _windowRect.y = Mathf.Clamp(_windowRect.y, -20f, Screen.height / scale - 60f);
                }
                else if (_resizingWindow)
                {
                    // 绝对坐标 resize:右下角跟随鼠标
                    _windowRect.width = Mathf.Max(360f, _dragStartWinPos.x + (e.mousePosition.x - _dragStartMouse.x));
                    _windowRect.height = Mathf.Max(300f, _dragStartWinPos.y + (e.mousePosition.y - _dragStartMouse.y));
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _draggingWindow = false;
                _resizingWindow = false;
                GUIUtility.hotControl = 0;
            }

            // 画 resize 手柄提示(窗口局部坐标绘制)
            var handleLocal = new Rect(_windowRect.width - handleSize, _windowRect.height - handleSize, handleSize, handleSize);
            if (_resizingWindow || handleRect.Contains(e.mousePosition))
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(handleLocal, Texture2D.whiteTexture);
                GUI.color = oldColor;
            }
        }

        /// <summary>绘制配置窗口背景图(plugins/KKPEHeightLock/background.png,可选)。</summary>
        private static void DrawWindowBackground()
        {
            if (!_backgroundAttempted)
            {
                _backgroundAttempted = true;
                try
                {
                    var bgPath = System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "KKPEHeightLock"), "background.png");
                    if (System.IO.File.Exists(bgPath))
                    {
                        _windowBackground = PngAssist.LoadTexture(bgPath);
                        if (_windowBackground != null)
                            KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: using window background image");
                    }
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to load background image: " + e.Message);
                }
            }

            if (_windowBackground != null)
            {
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), _windowBackground, ScaleMode.StretchToFill, true);
                // 极淡深色遮罩保证文字可读,但不发白
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
            else
            {
                // 无背景图时:深色半透明底,避免刺眼白
                GUI.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
        }

        /// <summary>把当前在工作室中选中的角色替换为指定卡(走 ChangeChara,自动应用姿势/身材/服装保留)。</summary>
        internal static int ReplaceSelectedCharacter(string cardPath)
        {
            int replaced = 0;
            try
            {
                if (string.IsNullOrEmpty(cardPath) || !System.IO.File.Exists(cardPath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: card not found: {cardPath}");
                    return 0;
                }
                // 遍历当前选中对象,替换其中所有角色
                var selected = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                if (selected == null) return 0;
                foreach (var obj in selected)
                {
                    var oci = obj as Studio.OCIChar;
                    if (oci == null || oci.charInfo == null) continue;
                    KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replacing selected character with {cardPath}");
                    oci.ChangeChara(cardPath);
                    replaced++;
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: ReplaceSelectedCharacter error: " + e);
            }
            return replaced;
        }

        /// <summary>替换选中角色:必须恰好选中 1 个角色才执行,否则提示并拒绝。
        /// 用于"替换选中(单个)"按钮,避免误替换全部。</summary>
        internal static bool ReplaceSingleSelectedCharacter(string cardPath)
        {
            try
            {
                if (string.IsNullOrEmpty(cardPath) || !System.IO.File.Exists(cardPath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: card not found: {cardPath}");
                    return false;
                }

                // 收集当前所有选中的角色
                var selectedChars = new System.Collections.Generic.List<Studio.OCIChar>();
                var selected = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                if (selected != null)
                {
                    foreach (var obj in selected)
                    {
                        var oci = obj as Studio.OCIChar;
                        if (oci != null && oci.charInfo != null)
                            selectedChars.Add(oci);
                    }
                }

                if (selectedChars.Count == 0)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: no character selected — 请先在工作室中选中要替换的角色");
                    return false;
                }
                if (selectedChars.Count > 1)
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: {selectedChars.Count} characters selected — 替换选中(单个)要求恰好选中 1 个角色,已取消");
                    return false;
                }

                var target = selectedChars[0];
                KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replacing single selected character with {cardPath}");
                target.ChangeChara(cardPath);
                return true;
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: ReplaceSingleSelectedCharacter error: " + e);
                return false;
            }
        }

        /// <summary>选卡窗口的根目录(相对 UserData):女/男卡目录。</summary>
        internal static string CardPickerBaseDir()
        {
            return _cardPickerIsFemale ? "chara/female" : "chara/male";
        }

        /// <summary>设置选卡窗口的当前目录(相对根)。</summary>
        internal static void SetCardPickerDir(string path)
        {
            _cardPickerCurrentDir = path ?? "";
            LoadCardList();
        }

        /// <summary>打开子目录选择窗口(0=场景 1=随机女 2=随机男 3=选卡)。</summary>
        private static void OpenSubdirPicker(int mode)
        {
            _subdirPickerMode = mode;
            _subdirPickerPath = "";
            _subdirPickerVisible = true;
            _subdirScroll = Vector2.zero;
            RefreshSubdirList();
        }

        private static void RefreshSubdirList()
        {
            _subdirList = ScenePlayerModule.ListSubdirsAt(_subdirPickerMode, _subdirPickerPath);
        }

        /// <summary>构建高对比皮肤:深色背景上,按钮亮色底+白字,文字白色,更醒目。</summary>
        /// <summary>原生皮肤 + 字体放大(不改任何颜色/背景,保留 Unity 默认样式)。</summary>
        private static GUISkin BuildVRSkin(GUISkin baseSkin)
        {
            var skin = UnityEngine.Object.Instantiate(baseSkin);
            int add = 3; // 字号放大

            // 按钮:保持原生浅色立体样式(醒目),文字深色,不加任何染色
            var btn = new GUIStyle(skin.button);
            btn.fontSize = Mathf.Max(14, baseSkin.button.fontSize + add);
            skin.button = btn;

            // 标签:白色(深色背景上清晰)
            var lbl = new GUIStyle(skin.label);
            lbl.fontSize = Mathf.Max(14, baseSkin.label.fontSize + add);
            lbl.normal.textColor = Color.white;
            skin.label = lbl;

            // Toggle:白色
            var tog = new GUIStyle(skin.toggle);
            tog.fontSize = Mathf.Max(14, baseSkin.toggle.fontSize + add);
            tog.normal.textColor = Color.white;
            skin.toggle = tog;

            // 文本框:白字
            var tf = new GUIStyle(skin.textField);
            tf.fontSize = Mathf.Max(14, baseSkin.textField.fontSize + add);
            tf.normal.textColor = Color.white;
            skin.textField = tf;

            // 框:白字
            var box = new GUIStyle(skin.box);
            box.fontSize = Mathf.Max(14, baseSkin.box.fontSize + add);
            box.normal.textColor = Color.white;
            skin.box = box;

            // 窗口:白字标题
            var win = new GUIStyle(skin.window);
            win.fontSize = Mathf.Max(14, baseSkin.window.fontSize + add);
            win.normal.textColor = Color.white;
            skin.window = win;

            return skin;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> _solidTexCache =
            new System.Collections.Generic.Dictionary<int, Texture2D>();

        /// <summary>创建垂直渐变纹理(顶部 top → 底部 bottom),带缓存。</summary>
        private static Texture2D MakeGradientTexture(Color top, Color bottom)
        {
            string key = "g_" + top.r.ToString("0.00") + "_" + top.g.ToString("0.00") + "_" + top.b.ToString("0.00") + "_" + top.a.ToString("0.00") +
                         "_" + bottom.r.ToString("0.00") + "_" + bottom.b.ToString("0.00");
            int hash = key.GetHashCode();
            Texture2D tex;
            if (!_solidTexCache.TryGetValue(hash, out tex))
            {
                const int h = 8;
                tex = new Texture2D(1, h, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                for (int y = 0; y < h; y++)
                {
                    float t = (float)y / (h - 1);
                    tex.SetPixel(0, y, Color.Lerp(top, bottom, t));
                }
                tex.Apply();
                _solidTexCache[hash] = tex;
            }
            return tex;
        }

        /// <summary>创建圆角渐变按钮纹理(抗锯齿圆角 + 顶部高光 + 渐变,接近原版立体感)。
        /// 16x16 生成,配合 9-slice 缩放。</summary>
        private static Texture2D MakeRoundedGradientTexture(Color top, Color bottom, Color highlight, int size = 16, int corner = 5)
        {
            string key = "rr_" + top.r.ToString("0.00") + "_" + top.b.ToString("0.00") + "_" + bottom.r.ToString("0.00") + "_" + bottom.g.ToString("0.00") + "_" + size + "_" + corner;
            int hash = key.GetHashCode();
            Texture2D tex;
            if (!_solidTexCache.TryGetValue(hash, out tex))
            {
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                for (int y = 0; y < size; y++)
                {
                    float t = (float)y / (size - 1);
                    Color grad = Color.Lerp(top, bottom, t);
                    // 顶部 1/3 叠加高光(原版立体感)
                    if (y < size / 3f)
                    {
                        float ht = 1f - (y / (size / 3f));
                        grad = Color.Lerp(grad, Color.white, ht * 0.25f);
                    }
                    for (int x = 0; x < size; x++)
                    {
                        // 四角圆角距离(抗锯齿:距角 < corner 时 alpha 平滑过渡)
                        float dx = Mathf.Min(x, size - 1 - x);
                        float dy = Mathf.Min(y, size - 1 - y);
                        bool inCornerQuadrant = (x < corner && y < corner) || (x < corner && y >= size - corner) ||
                                                (x >= size - corner && y < corner) || (x >= size - corner && y >= size - corner);
                        if (inCornerQuadrant)
                        {
                            // 到角顶点的距离
                            float cx = x < size / 2f ? x : size - 1 - x;
                            float cy = y < size / 2f ? y : size - 1 - y;
                            float distToCorner = Mathf.Sqrt(cx * cx + cy * cy);
                            // 距离 < corner-1 全透明,corner-1~corner 抗锯齿过渡
                            float alpha = Mathf.Clamp01((distToCorner - (corner - 1)) / 1f);
                            Color c = grad;
                            c.a *= alpha;
                            tex.SetPixel(x, y, c);
                        }
                        else
                        {
                            tex.SetPixel(x, y, grad);
                        }
                    }
                }
                tex.Apply();
                _solidTexCache[hash] = tex;
            }
            return tex;
        }

        /// <summary>按颜色缓存 1x1 纯色纹理(每种颜色独立,避免共享覆盖)。</summary>
        private static Texture2D MakeSolidTexture(Color c)
        {
            string key = (c.r * 255f).ToString("0") + "_" + (c.g * 255f).ToString("0") + "_" + (c.b * 255f).ToString("0") + "_" + (c.a * 255f).ToString("0");
            int hash = key.GetHashCode();
            Texture2D tex;
            if (!_solidTexCache.TryGetValue(hash, out tex))
            {
                tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.SetPixel(0, 0, c);
                tex.Apply();
                _solidTexCache[hash] = tex;
            }
            return tex;
        }

        /// <summary>功能区标题:双色渐变背景 + 白字(每个分类不同配色)。</summary>
        /// <summary>功能区标题:原生 box 样式(忽略配色参数,保持原生)。</summary>
        private static void DrawSectionBox(string title, Color top, Color bottom)
        {
            GUILayout.Label(title, GUI.skin.box);
        }

        /// <summary>服装部件三态行(参照 sceneplayer):穿/半脱/脱。</summary>
        private static void DrawClothesPartRow(string partName, int partIndex)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(partName, GUILayout.Width(44));
            var chara = ScenePlayerModule.CurrentEditChar();
            if (GUILayout.Button("穿", GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 0);
            if (GUILayout.Button("半脱", GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 1);
            if (GUILayout.Button("脱", GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 2);
            GUILayout.EndHorizontal();
        }

        /// <summary>子目录选择窗口:列出当前层级子文件夹,点进下一级,可返回上级,选择/根目录/取消。</summary>
        private static void DrawSubdirPicker(int id)
        {
            GUILayout.BeginVertical();

            // 当前路径
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↑上级", GUILayout.Width(56), GUILayout.Height(28)))
            {
                // 返回上级
                int idx = _subdirPickerPath.LastIndexOf('/');
                _subdirPickerPath = idx >= 0 ? _subdirPickerPath.Substring(0, idx) : "";
                RefreshSubdirList();
            }
            GUILayout.Label("路径: " + (string.IsNullOrEmpty(_subdirPickerPath) ? "根目录" : _subdirPickerPath), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(380), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 子文件夹列表(垂直列出,不横向溢出)
            _subdirScroll = GUILayout.BeginScrollView(_subdirScroll);
            if (_subdirList.Count == 0)
            {
                GUILayout.Label("(此目录下没有子文件夹)");
            }
            else
            {
                foreach (var sub in _subdirList)
                {
                    if (GUILayout.Button("📁 " + sub, GUILayout.Height(30)))
                    {
                        // 进入下一级
                        _subdirPickerPath = string.IsNullOrEmpty(_subdirPickerPath) ? sub : _subdirPickerPath + "/" + sub;
                        RefreshSubdirList();
                        break;
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // 底部操作:选择当前路径 / 根目录 / 取消
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择此目录", GUILayout.Height(32)))
            {
                ScenePlayerModule.ApplySubdir(_subdirPickerMode, _subdirPickerPath);
                _subdirPickerVisible = false;
            }
            if (GUILayout.Button("根目录", GUILayout.Height(32)))
            {
                ScenePlayerModule.ApplySubdir(_subdirPickerMode, "");
                _subdirPickerVisible = false;
            }
            if (GUILayout.Button("取消", GUILayout.Height(32)))
                _subdirPickerVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>打开游戏内选卡窗口(类似工作室本体,带缩略图列表)。</summary>
        private static void PickCard(bool isFemale)
        {
            try
            {
                _cardPickerIsFemale = isFemale;
                _cardPickerVisible = true;
                _cardScroll = Vector2.zero;
                _cardPickerCurrentDir = "";
                _cardSearch = "";
                LoadCardList();
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: PickCard error: " + e);
            }
        }

        /// <summary>只读取当前目录下的角色卡和直接子文件夹(不递归),卡多也不卡。</summary>
        private static void LoadCardList()
        {
            _cardEntries = new System.Collections.Generic.List<CardEntry>();
            _cardSubFolders = new System.Collections.Generic.List<string>();
            _cardScroll = Vector2.zero;
            try
            {
                string baseDir = System.IO.Path.Combine(UserData.Path, _cardPickerIsFemale ? "chara/female" : "chara/male");
                string currentDir = string.IsNullOrEmpty(_cardPickerCurrentDir)
                    ? baseDir
                    : System.IO.Path.Combine(baseDir, _cardPickerCurrentDir);

                // 直接子文件夹(仅当前层级)
                foreach (var d in System.IO.Directory.GetDirectories(currentDir))
                {
                    string name = System.IO.Path.GetFileName(d);
                    if (name.Length > 0)
                        _cardSubFolders.Add(name);
                }
                _cardSubFolders.Sort(StringComparer.OrdinalIgnoreCase);

                // 当前目录的直接角色卡
                foreach (var f in System.IO.Directory.GetFiles(currentDir, "*.png"))
                    _cardEntries.Add(new CardEntry { path = f });

                KKPEHeightLockPlugin.Log.LogInfo($"KKPEHeightLock: folder '{_cardPickerCurrentDir}': {_cardEntries.Count} cards, {_cardSubFolders.Count} subfolders");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: LoadCardList error: " + e);
            }
        }

        /// <summary>懒加载入口:每帧最多处理若干张卡的元数据/缩略图,避免一帧卡死。</summary>
        private static void LazyLoadCards(int maxPerFrame)
        {
            _cardsProcessedThisFrame = 0;
            if (_cardEntries == null) return;
            foreach (var entry in _cardEntries)
            {
                if (_cardsProcessedThisFrame >= maxPerFrame) return;

                // 只处理搜索条件下会显示的卡
                if (!string.IsNullOrEmpty(_cardSearch))
                {
                    string nm = entry.name ?? System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    if (nm.IndexOf(_cardSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                // 1) 角色名(元数据)懒加载
                if (!entry.metaLoaded)
                {
                    try
                    {
                        var chaFile = new ChaFileControl();
                        if (chaFile.LoadCharaFile(entry.path, 255, true, true))
                            entry.name = chaFile.parameter != null && !string.IsNullOrEmpty(chaFile.parameter.fullname)
                                ? chaFile.parameter.fullname
                                : System.IO.Path.GetFileNameWithoutExtension(entry.path);
                        else
                            entry.name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    }
                    catch (Exception)
                    {
                        entry.name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    }
                    entry.metaLoaded = true;
                    _cardsProcessedThisFrame++;
                    continue;
                }

                // 2) 缩略图懒加载(显示到的才加载)
                if (entry.texture == null)
                {
                    try { entry.texture = PngAssist.LoadTexture(entry.path); }
                    catch (Exception) { }
                    _cardsProcessedThisFrame++;
                    continue;
                }
            }
        }

        /// <summary>选卡窗口:目录导航 + 搜索框 + 缩略图网格(懒加载)。</summary>
        private static void DrawCardPicker(int id)
        {
            GUILayout.BeginVertical();

            // 每帧懒加载最多 6 张(元数据或缩略图),保持流畅
            LazyLoadCards(6);

            // --- 目录导航 ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← 上级", GUILayout.Width(70)))
            {
                if (_cardPickerCurrentDir.Length > 0)
                {
                    int idx = _cardPickerCurrentDir.LastIndexOfAny(new[] { '\\', '/' });
                    _cardPickerCurrentDir = idx >= 0 ? _cardPickerCurrentDir.Substring(0, idx) : "";
                    LoadCardList();
                }
            }
            GUILayout.Label("目录: " + (_cardPickerCurrentDir.Length > 0 ? _cardPickerCurrentDir : "(根目录)"), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // --- 子文件夹(点"选择子文件夹"弹出子目录窗口) ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择子文件夹", GUILayout.Width(96), GUILayout.Height(26)))
                OpenSubdirPicker(3);
            GUILayout.Label("目录: " + (_cardPickerCurrentDir.Length > 0 ? _cardPickerCurrentDir : "(根目录)"), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // --- 搜索框 ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索:", GUILayout.Width(55));
            _cardSearch = GUILayout.TextField(_cardSearch ?? "", GUILayout.MinWidth(200));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // --- 卡片网格 ---
            if (_cardEntries == null || _cardEntries.Count == 0)
            {
                GUILayout.Label("当前目录下没有角色卡。");
            }
            else
            {
                // 搜索过滤
                var filtered = new System.Collections.Generic.List<CardEntry>();
                foreach (var e in _cardEntries)
                {
                    if (!string.IsNullOrEmpty(_cardSearch))
                    {
                        string nm = e.name ?? System.IO.Path.GetFileNameWithoutExtension(e.path);
                        if (nm.IndexOf(_cardSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    }
                    filtered.Add(e);
                }

                if (filtered.Count == 0)
                {
                    GUILayout.Label("没有匹配的卡。");
                }
                else
                {
                    _cardScroll = GUILayout.BeginScrollView(_cardScroll);

                    const float thumbWidth = 128f;
                    int columns = Mathf.Max(1, (int)((_cardPickerRect.width - 40f) / (thumbWidth + 8f)));

                    int index = 0;
                    while (index < filtered.Count)
                    {
                        GUILayout.BeginHorizontal();
                        for (int c = 0; c < columns && index < filtered.Count; c++, index++)
                        {
                            var entry = filtered[index];
                            GUILayout.BeginVertical(GUILayout.Width(thumbWidth));

                            // 缩略图(懒加载)
                            if (entry.texture != null)
                                GUILayout.Label(entry.texture, GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth));
                            else
                                GUILayout.Box("…", GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth));

                            string displayName = entry.name ?? System.IO.Path.GetFileNameWithoutExtension(entry.path);
                            GUILayout.Label(Truncate(displayName, 14), GUILayout.Width(thumbWidth));

                            // 行1:选择(设置路径)
                            if (GUILayout.Button("选择", GUILayout.Width(thumbWidth)))
                            {
                                if (_cardPickerIsFemale)
                                    KKPEHeightLockPlugin.FemaleCardPath.Value = entry.path;
                                else
                                    KKPEHeightLockPlugin.MaleCardPath.Value = entry.path;
                                KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: {(_cardPickerIsFemale ? "female" : "male")} card set to {entry.path}");
                                _cardPickerVisible = false;
                            }
                            // 行2:直接替换当前选中的角色(替换所有选中的角色)
                            if (GUILayout.Button("直接替换", GUILayout.Width(thumbWidth)))
                            {
                                int replaced = ReplaceSelectedCharacter(entry.path);
                                if (replaced > 0)
                                {
                                    KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replaced {replaced} selected character(s) with {entry.path}");
                                    _cardPickerVisible = false;
                                }
                                else
                                {
                                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: no character selected in studio, nothing to replace");
                                }
                            }
                            // 行3:替换选中角色(必须恰好选中 1 个角色才执行)
                            if (GUILayout.Button("替换选中(单个)", GUILayout.Width(thumbWidth)))
                            {
                                if (ReplaceSingleSelectedCharacter(entry.path))
                                {
                                    KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replaced the single selected character with {entry.path}");
                                    _cardPickerVisible = false;
                                }
                            }

                            GUILayout.EndVertical();
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndScrollView();
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("关闭"))
                _cardPickerVisible = false;
            GUILayout.Label($"{_cardEntries.Count} 张卡", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>绘制 [-] 值 [+] 步进行(替代滑块,VR 菜单里按钮更易点)。</summary>
        private static void DrawStepRow(string label, ConfigEntry<float> entry, float step, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            if (GUILayout.Button("−", GUILayout.Width(36), GUILayout.Height(32)))
                entry.Value = Mathf.Max(min, entry.Value - step);
            GUILayout.Label(entry.Value.ToString(format), GUI.skin.textField, GUILayout.Width(60), GUILayout.Height(32));
            if (GUILayout.Button("＋", GUILayout.Width(36), GUILayout.Height(32)))
                entry.Value = Mathf.Min(max, entry.Value + step);
            GUILayout.EndHorizontal();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>按窗口宽度动态截断路径,保证不把"选择"按钮挤出窗口。
        /// 窗口宽 560 时 label 60 + button 60 + 边距,textField 可用约 400px,约 55 个字符。</summary>
        private static string FitPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(未选择)";
            // 随窗口宽度自适应
            int maxChars = Mathf.Max(12, (int)((_windowRect.width - 170f) / 7.2f));
            if (path.Length <= maxChars) return path;
            return "..." + path.Substring(path.Length - (maxChars - 3));
        }
    }

    /// <summary>
    /// 场景工具:去无用球体 + 去码。
    /// 逻辑参照 KK_SphereRemover v1.0.0 与 KK_StudioDecensor。
    /// </summary>
    public static class SceneTools
    {
        /// <summary>隐藏工作室里的无用球体(灯光/阴影辅助球)。参照 KK_SphereRemover。</summary>
        public static void RemoveUselessSpheres()
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicInfo == null) return;

                int removed = 0;
                foreach (var kv in studio.dicInfo)
                {
                    var item = kv.Value as Studio.OCIItem;
                    if (item == null || item.objectItem == null) continue;

                    // 球体判断:renderer 名为 item_O_Sphere
                    var renderers = MaterialEditorAPI.MaterialAPI.GetRendererList(item.objectItem);
                    if (renderers == null) continue;
                    var rlist = new System.Collections.Generic.List<UnityEngine.Renderer>(renderers);
                    if (rlist.Count == 0) continue;
                    var r0 = rlist[0];
                    if (r0 == null || r0.name != "item_O_Sphere") continue;

                    // 材质判断:材质名以 m_koi_stu_kihon01_02 开头(空格分割取首段)
                    var mats = MaterialEditorAPI.MaterialAPI.GetMaterials(item.objectItem, r0);
                    if (mats == null) continue;
                    var mlist = new System.Collections.Generic.List<UnityEngine.Material>(mats);
                    if (mlist.Count == 0) continue;
                    var m0 = mlist[0];
                    if (m0 == null) continue;
                    string matName = m0.name.Split(' ')[0];
                    if (matName != "m_koi_stu_kihon01_02") continue;

                    // renderQueue 判断(与原插件一致)
                    if (m0.renderQueue != 4907) continue;

                    // 隐藏该球体
                    kv.Key.SetVisible(false);
                    removed++;
                }
                KKPEHeightLockPlugin.Log.LogMessage($"SceneTools: removed {removed} useless sphere(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.RemoveUselessSpheres error: " + e);
            }
        }

        /// <summary>去码:移除马赛克/圣光材质。参照 KK_StudioDecensor。</summary>
        public static void DecensorScene()
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicInfo == null) return;

                int fixedCount = 0;
                foreach (var kv in studio.dicInfo)
                {
                    var oci = kv.Value as Studio.OCIItem;
                    if (oci == null || oci.objectItem == null) continue;

                    var renderers = MaterialEditorAPI.MaterialAPI.GetRendererList(oci.objectItem);
                    if (renderers == null) continue;
                    var rlist = new System.Collections.Generic.List<UnityEngine.Renderer>(renderers);
                    foreach (var r in rlist)
                    {
                        if (r == null) continue;
                        var mats = MaterialEditorAPI.MaterialAPI.GetMaterials(oci.objectItem, r);
                        if (mats == null) continue;
                        foreach (var mat in mats)
                        {
                            if (mat == null) continue;
                            bool changed = false;
                            // 材质名去掉 (Instance) 后缀再 Trim
                            string name = mat.name.Replace("(Instance)", "").Trim();

                            // 1) 像素马赛克:shader 是 Custom/Pixelate → PixelSize 设为 1(不马赛克)
                            if (mat.shader != null && mat.shader.name == "Custom/Pixelate")
                            {
                                MaterialEditorAPI.MaterialAPI.SetFloat(oci.objectItem, name, "PixelSize", 1f);
                                changed = true;
                            }
                            // 2) 圣光/模糊:材质名以 CensorEffectMat 或 blur_ 开头 → 换透明 shader 并 alpha=0
                            else if (name.StartsWith("CensorEffectMat") || name.StartsWith("blur_"))
                            {
                                MaterialEditorAPI.MaterialAPI.SetShader(oci.objectItem, name, "Shader Forge/main_item_studio_alpha", false);
                                MaterialEditorAPI.MaterialAPI.SetFloat(oci.objectItem, name, "alpha", 0f);
                                changed = true;
                            }

                            if (changed)
                                fixedCount++;
                        }
                    }
                }
                KKPEHeightLockPlugin.Log.LogMessage($"SceneTools: decensored {fixedCount} material(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.DecensorScene error: " + e);
            }
        }
    }

    /// <summary>
    /// 第一人称视角(VR 优先,桌面兼容)。
    /// - 相机跟随选中角色头部(objHeadBone),带前后偏移
    /// - VR 下右手摇杆(Axis0)控制垂直/水平转头,摇杆推到底手柄朝向即视角(万向)
    /// - 每帧按 CharaId 从场景取角色(不缓存),换场景/动作仍跟随
    /// - 角色消失时按记录的角色名扫描场景找同名角色自动切换
    /// </summary>
    public static class VRFirstPersonPOV
    {
        // 跟随状态
        private static int _charaId = -1;
        private static string _charaName;
        private static Vector3 _lookEuler;
        private static bool _wasEnabled;
        private static ChaControl _lastHiddenChara; // 上一个被隐藏头部的角色(关闭/切换时恢复)

        // LateUpdate 二次应用 rig 用(SteamVR 追踪更新后)
        private static Vector3 _lastCamPos;
        private static bool _lastWasVR;

        // rig 原始 transform(进入 POV 前保存,关闭时恢复位置/旋转)
        private static Vector3 _savedRigPos;
        private static Quaternion _savedRigRot;
        private static bool _rigSaved;

        public static void UpdatePOV(bool enabled)
        {
            try
            {
                // VR:左手手柄 Y 键(OpenVR 中为左手的 A 键)切换第一人称开关
                CheckLeftYButtonToggle();

                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;

                if (!enabled)
                {
                    if (_wasEnabled)
                    {
                        KKPEHeightLockPlugin.Log.LogMessage("VRFirstPersonPOV: disabled");
                        RestoreHiddenHead(); // 恢复被隐藏的头部
                        RestoreRigTransform(); // 复原 rig 位置/旋转,不再锁在第一人称原处
                    }
                    _wasEnabled = false;
                    _charaId = -1;
                    return;
                }

                // 找到跟随角色
                var chara = FindFollowCharacter(studio);
                if (chara == null || chara.charInfo == null)
                {
                    if (_wasEnabled)
                    {
                        // 角色被隐藏/删除/消失:自动退出第一人称,避免视角锁在原地
                        KKPEHeightLockPlugin.Log.LogMessage("VRFirstPersonPOV: target character gone, auto-disable POV");
                        RestoreHiddenHead();
                        RestoreRigTransform();
                        KKPEHeightLockPlugin.POVEnabled.Value = false;
                    }
                    _wasEnabled = false;
                    _charaId = -1;
                    return;
                }

                if (!_wasEnabled)
                {
                    _wasEnabled = true;
                    // 保存 rig 原始 transform(关闭时恢复)
                    SaveRigTransform();
                    // 初始化视角朝向
                    if (IsVRModeActive())
                    {
                        // VR:记录 rig 当前朝向(SteamVR 原始朝向),之后由摇杆接管
                        try
                        {
                            var origin = VRGIN.Core.VRCamera.Instance.Origin;
                            if (origin != null)
                                _lookEuler = origin.rotation.eulerAngles;
                            else
                                _lookEuler = Vector3.zero;
                        }
                        catch (Exception)
                        {
                            _lookEuler = Vector3.zero;
                        }
                    }
                    else
                    {
                        var head = chara.charInfo.objHeadBone;
                        if (head != null)
                            _lookEuler = head.transform.rotation.eulerAngles;
                    }
                    // 隐藏当前角色头部,避免穿模
                    HideHead(chara.charInfo);
                    KKPEHeightLockPlugin.Log.LogMessage($"VRFirstPersonPOV: following {chara.charInfo.fileParam?.fullname ?? _charaName}");
                }
                else if (_lastHiddenChara != null && _lastHiddenChara != chara.charInfo)
                {
                    // 跟随角色已切换(同名自动跟随):恢复旧角色头部,隐藏新角色头部
                    RestoreHiddenHead();
                    HideHead(chara.charInfo);
                }

                // 每帧取最新眼睛位置(RealPOV 同款:双眼中点,不缓存 Transform)
                var eyePos = FindEyePosition(chara.charInfo);
                var headBone = chara.charInfo.objHeadBone;
                if (eyePos == null && headBone == null) return;
                var camPos = eyePos ?? headBone.transform.position;

                // 应用垂直高低偏移(正=向上,负=向下)
                camPos.y += KKPEHeightLockPlugin.POVHeightOffset.Value;

                // 应用左右偏移(沿当前视角的右方向)
                var rightDir = Quaternion.Euler(_lookEuler) * Vector3.right;
                camPos += rightDir * KKPEHeightLockPlugin.POVLateralOffset.Value;

                if (IsVRModeActive())
                {
                    // VR:位置钉眼睛(+高低/视角偏移),摇杆转头 + FOV 由 TryApplyVRRig 处理
                    var fwd = Quaternion.Euler(_lookEuler) * Vector3.forward;
                    camPos += fwd * KKPEHeightLockPlugin.POVViewOffset.Value;
                    _lastCamPos = camPos;   // 供 LateUpdate 二次应用(SteamVR 追踪更新后)
                    _lastWasVR = true;
                    TryApplyVRRig(camPos, ref _lookEuler);
                }
                else
                {
                    // 桌面:相机放到眼睛位置,朝向 _lookEuler,摇杆增量转头
                    ApplyControllerLook();
                    var cam = GetCamera();
                    if (cam != null)
                    {
                        var fwd = Quaternion.Euler(_lookEuler) * Vector3.forward;
                        cam.transform.position = camPos + fwd * KKPEHeightLockPlugin.POVViewOffset.Value;
                        cam.transform.rotation = Quaternion.Euler(_lookEuler);
                        if (Mathf.Abs(cam.fieldOfView - KKPEHeightLockPlugin.POVFOV.Value) > 0.01f)
                            cam.fieldOfView = KKPEHeightLockPlugin.POVFOV.Value;
                    }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRFirstPersonPOV error: " + e);
            }
        }

        /// <summary>查找角色双眼位置的中点(RealPOV 同款:取左右眼 eyeTransform 的中点)。
        /// 找不到眼睛时回退到头部骨骼位置。</summary>
        private static Vector3? FindEyePosition(ChaControl chara)
        {
            try
            {
                var eyeCtrl = chara.eyeLookCtrl;
                if (eyeCtrl != null && eyeCtrl.eyeLookScript != null)
                {
                    var eyeObjs = eyeCtrl.eyeLookScript.eyeObjs;
                    if (eyeObjs != null && eyeObjs.Length >= 2 &&
                        eyeObjs[0] != null && eyeObjs[1] != null &&
                        eyeObjs[0].eyeTransform != null && eyeObjs[1].eyeTransform != null)
                    {
                        // 双眼中点
                        return Vector3.Lerp(eyeObjs[0].eyeTransform.position, eyeObjs[1].eyeTransform.position, 0.5f);
                    }
                }

                // 回退:头部骨骼位置
                var headBone = chara.objHeadBone;
                if (headBone != null) return headBone.transform.position;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>检测 VR 相机 rig 是否可用(仅 VR 使用,桌面端不保证)。</summary>
        private static bool IsVRModeActive()
        {
            try
            {
                var vrcam = VRGIN.Core.VRCamera.Instance;
                return vrcam != null && vrcam.Origin != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>LateUpdate 再次应用 rig:SteamVR 在 Update 中按追踪更新头显 pose,
        /// 我们随后在 LateUpdate 重新钉位置+清零头显局部偏移,确保俯仰/偏航转头都不移动摄像机位置。</summary>
        public static void LateUpdatePOV()
        {
            try
            {
                if (!KKPEHeightLockPlugin.POVEnabled.Value)
                {
                    _lastWasVR = false;
                    return;
                }
                if (_lastWasVR)
                    TryApplyVRRig(_lastCamPos, ref _lookEuler);
            }
            catch (Exception) { }
        }

        /// <summary>VR 下把整个 VRCamera rig 移动到眼睛位置。
        /// 位置钉在眼睛中点;朝向由 _lookEuler 控制(绝对设置),摇杆 x→左右偏航、y→上下俯仰。</summary>
        private static bool TryApplyVRRig(Vector3 worldPos, ref Vector3 lookEuler)
        {
            try
            {
                var vrcam = VRGIN.Core.VRCamera.Instance;
                if (vrcam == null) return false;
                var origin = vrcam.Origin;
                if (origin == null) return false;

                // 位置钉在眼睛中点(每帧)
                origin.position = worldPos;

                // 摇杆增量转头:累加到 _lookEuler
                var dev = GetRightController();
                if (dev != null && dev.valid)
                {
                    var axis = dev.GetAxis(Valve.VR.EVRButtonId.k_EButton_Axis0);
                    float sens = KKPEHeightLockPlugin.POVLookSpeed.Value;
                    float stickMag = new Vector2(axis.x, axis.y).magnitude;
                    if (stickMag > 0.15f)
                    {
                        // x→偏航(左右), y→俯仰(上下)
                        lookEuler.y = NormalizeAngle(lookEuler.y + axis.x * sens);
                        lookEuler.x = NormalizeAngle(lookEuler.x - axis.y * sens);
                    }
                }

                // 绝对设置朝向(可转)
                origin.rotation = Quaternion.Euler(lookEuler);

                // 关键:补偿头显相对原点的偏移,让头显世界位置恒 = 角色眼睛(worldPos)。
                // 俯仰/偏航转头时,偏移被旋转放大会导致头显绕圈/飞走,这里每帧平移原点把它钉死。
                try
                {
                    var head = vrcam.Head;
                    if (head != null)
                    {
                        Vector3 local = origin.InverseTransformPoint(head.position); // 头显相对原点的偏移(当前朝向坐标系)
                        origin.position = worldPos - origin.TransformVector(local);  // 平移原点,使头显世界位置 = worldPos
                    }
                    else
                    {
                        origin.position = worldPos;
                    }
                }
                catch (Exception)
                {
                    origin.position = worldPos;
                }

                // 应用 FOV 到 VR 相机
                try
                {
                    var steamCam = vrcam.SteamCam;
                    if (steamCam != null)
                    {
                        var cam = steamCam.GetComponent<Camera>();
                        if (cam != null && Mathf.Abs(cam.fieldOfView - KKPEHeightLockPlugin.POVFOV.Value) > 0.01f)
                            cam.fieldOfView = KKPEHeightLockPlugin.POVFOV.Value;
                    }
                }
                catch (Exception) { }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>保存 rig 原始 transform(进入 POV 前);桌面保存 Camera.main。</summary>
        private static void SaveRigTransform()
        {
            try
            {
                if (IsVRModeActive())
                {
                    var origin = VRGIN.Core.VRCamera.Instance?.Origin;
                    if (origin != null)
                    {
                        _savedRigPos = origin.position;
                        _savedRigRot = origin.rotation;
                        _rigSaved = true;
                    }
                }
                else
                {
                    // 桌面:保存 Camera.main 的 transform
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        _savedRigPos = cam.transform.position;
                        _savedRigRot = cam.transform.rotation;
                        _rigSaved = true;
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>恢复 rig/Camera.main 原始 transform(关闭 POV 时,不锁在第一人称原处)。</summary>
        private static void RestoreRigTransform()
        {
            try
            {
                if (!_rigSaved) return;
                if (IsVRModeActive())
                {
                    var origin = VRGIN.Core.VRCamera.Instance?.Origin;
                    if (origin != null)
                    {
                        origin.position = _savedRigPos;
                        origin.rotation = _savedRigRot;
                    }
                }
                else
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        cam.transform.position = _savedRigPos;
                        cam.transform.rotation = _savedRigRot;
                    }
                }
                _rigSaved = false;
            }
            catch (Exception) { }
        }

        /// <summary>隐藏指定角色的头部,避免第一人称相机穿模(RealPOV 同款方案)。
        /// visibleHeadAlways 由 ChaControl.LateUpdateForce 每帧消费,设置后下一帧自动生效。</summary>
        private static void HideHead(ChaControl chara)
        {
            try
            {
                if (chara == null) return;
                // 记录当前跟随角色,恢复时用
                _lastHiddenChara = chara;
                var status = chara.fileStatus;
                if (status != null)
                    status.visibleHeadAlways = false;
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRFirstPersonPOV: HideHead error: " + e.Message);
            }
        }

        /// <summary>恢复上一个被隐藏角色的头部。</summary>
        private static void RestoreHiddenHead()
        {
            try
            {
                if (_lastHiddenChara == null) return;
                var status = _lastHiddenChara.fileStatus;
                if (status != null)
                    status.visibleHeadAlways = true;
                _lastHiddenChara = null;
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("VRFirstPersonPOV: RestoreHiddenHead error: " + e.Message);
            }
        }

        /// <summary>从场景找跟随角色:优先当前选中的角色;没选中才回退缓存(CharaId/同名)。
        /// 避免"选了男性却跟随女性"——每次都以当前选中为准。</summary>
        private static Studio.OCIChar FindFollowCharacter(Studio.Studio studio)
        {
            // 1) 优先:当前选中的角色(用户明确选择的)
            var selected = KKAPI.Studio.StudioAPI.GetSelectedObjects();
            if (selected != null)
            {
                Studio.OCIChar picked = null;
                foreach (var obj in selected)
                {
                    var c = obj as Studio.OCIChar;
                    if (c != null && c.charInfo != null)
                    {
                        if (picked == null) picked = c; // 取第一个选中角色
                    }
                }
                if (picked != null)
                {
                    _charaId = FindIdOf(studio, picked);
                    _charaName = picked.charInfo.fileParam?.fullname;
                    return picked;
                }
            }

            // 2) 没选中:按缓存 id 找(原跟随角色仍在场景)
            if (_charaId >= 0)
            {
                Studio.ObjectCtrlInfo oci;
                if (studio.dicObjectCtrl.TryGetValue(_charaId, out oci) && oci is Studio.OCIChar)
                {
                    var c = (Studio.OCIChar)oci;
                    if (c.charInfo != null) return c;
                }
            }

            // 3) 缓存 id 失效:按名字找同名角色(原角色被隐藏/替换后自动跟随)
            if (!string.IsNullOrEmpty(_charaName))
            {
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var c = kv.Value as Studio.OCIChar;
                    if (c == null || c.charInfo == null) continue;
                    string n = c.charInfo.fileParam?.fullname;
                    if (n == _charaName)
                    {
                        _charaId = kv.Key;
                        return c;
                    }
                }
            }

            return null;
        }

        /// <summary>在场景字典中查找指定 OCIChar 的 key(CharaId)。</summary>
        private static int FindIdOf(Studio.Studio studio, Studio.OCIChar target)
        {
            if (studio?.dicObjectCtrl == null || target == null) return -1;
            foreach (var kv in studio.dicObjectCtrl)
            {
                if (kv.Value == target) return kv.Key;
            }
            return -1;
        }

        /// <summary>桌面模式:右手摇杆增量转头(摇杆 x→左右偏航,y→上下俯仰)。</summary>
        private static void ApplyControllerLook()
        {
            var dev = GetRightController();
            if (dev == null || !dev.valid) return;

            var axis = dev.GetAxis(Valve.VR.EVRButtonId.k_EButton_Axis0);
            float sens = KKPEHeightLockPlugin.POVLookSpeed.Value;
            float stickMag = new Vector2(axis.x, axis.y).magnitude;
            if (stickMag > 0.15f)
            {
                _lookEuler.x = NormalizeAngle(_lookEuler.x - axis.y * sens);
                _lookEuler.y = NormalizeAngle(_lookEuler.y + axis.x * sens);
            }
        }

        private static SteamVR_Controller.Device GetRightController()
        {
            try
            {
                int idx = SteamVR_Controller.GetDeviceIndex(SteamVR_Controller.DeviceRelation.Rightmost, Valve.VR.ETrackedDeviceClass.Controller, 0);
                if (idx < 0) return null;
                return SteamVR_Controller.Input(idx);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>检测左手手柄 Y 键按下(Quest 左手 Y = OpenVR 的 k_EButton_ApplicationMenu),呼出/关闭菜单。
        /// 注意:Quest 右手 B 键在 OpenVR 中也是 ApplicationMenu,必须用左手设备索引限定,避免右手误触。</summary>
        /// <summary>手柄按键处理:
        /// - 右手 B 键(ApplicationMenu) = 呼出/关闭悬浮菜单
        /// - 左手 Y 键(ApplicationMenu) = 第一人称 POV 开关
        /// (Quest 左右手柄的 Y/B 在 OpenVR 中都是 ApplicationMenu,必须用各自设备索引限定)</summary>
        private static void CheckLeftYButtonToggle()
        {
            try
            {
                // 右手 B → 菜单
                int rIdx = -1;
                try
                {
                    var mode = VRGIN.Core.VR.Mode;
                    if (mode != null && mode.Right != null)
                    {
                        var tracked = mode.Right.GetComponent<SteamVR_TrackedObject>();
                        if (tracked != null) rIdx = (int)tracked.index;
                    }
                }
                catch (Exception) { }
                if (rIdx < 0)
                {
                    rIdx = SteamVR_Controller.GetDeviceIndex(SteamVR_Controller.DeviceRelation.Rightmost, Valve.VR.ETrackedDeviceClass.Controller, 0);
                }
                if (rIdx >= 0)
                {
                    var rDev = SteamVR_Controller.Input(rIdx);
                    if (rDev != null && rDev.valid && rDev.GetPressDown(Valve.VR.EVRButtonId.k_EButton_ApplicationMenu))
                    {
                        // VR 模式切悬浮面板,桌面切 IMGUI 窗口
                        if (IsVRModeActive())
                            VRFloatingPanel.Toggle();
                        else
                            ConfigPanel.ToggleWristMenu();
                    }
                }

                // 左手 Y → POV 开关
                int lIdx = -1;
                try
                {
                    var mode = VRGIN.Core.VR.Mode;
                    if (mode != null && mode.Left != null)
                    {
                        var tracked = mode.Left.GetComponent<SteamVR_TrackedObject>();
                        if (tracked != null) lIdx = (int)tracked.index;
                    }
                }
                catch (Exception) { }
                if (lIdx < 0)
                {
                    lIdx = SteamVR_Controller.GetDeviceIndex(SteamVR_Controller.DeviceRelation.Leftmost, Valve.VR.ETrackedDeviceClass.Controller, 0);
                }
                if (lIdx >= 0)
                {
                    var lDev = SteamVR_Controller.Input(lIdx);
                    if (lDev != null && lDev.valid && lDev.GetPressDown(Valve.VR.EVRButtonId.k_EButton_ApplicationMenu))
                    {
                        KKPEHeightLockPlugin.POVEnabled.Value = !KKPEHeightLockPlugin.POVEnabled.Value;
                        KKPEHeightLockPlugin.Log.LogMessage("KKPEHeightLock: first-person POV " + (KKPEHeightLockPlugin.POVEnabled.Value ? "enabled" : "disabled"));
                    }
                }
            }
            catch (Exception)
            {
                // 非 VR 环境或手柄未连接时忽略
            }
        }

        private static Camera GetCamera()
        {
            // 仅 VR 真正活跃时用 PlayerCamera;桌面一律 Camera.main
            try
            {
                if (IsVRModeActive())
                {
                    var pc = UnityEngine.Object.FindObjectOfType<VRGIN.Visuals.PlayerCamera>();
                    if (pc != null)
                    {
                        var cam = pc.GetComponent<Camera>();
                        if (cam != null) return cam;
                    }
                }
            }
            catch (Exception) { }
            return Camera.main;
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }
    }

    /// <summary>
    /// 场景播放器(复刻 sceneplayer.py 核心功能):
    /// - 场景浏览/上一下一场景/加载/信息/删除(自然排序)
    /// - 随机替换角色(女/男/全部,保留身高)
    /// - 角色快速编辑(服装/Kinematic/身高/位置)
    /// - Timeline 控制(播放/暂停/停止/进度/时间缩放)
    /// - FSS 文件夹子场景切换
    /// </summary>
    public static class ScenePlayerModule
    {
        // 场景列表状态
        private static System.Collections.Generic.List<string> _sceneFiles = new System.Collections.Generic.List<string>();
        private static int _sceneIndex = -1;
        private static bool _scenesLoaded;

        // FSS 状态
        private static System.Collections.Generic.List<Studio.OCIChar> _fssChars = new System.Collections.Generic.List<Studio.OCIChar>();
        private static int _fssIndex = -1;
        private static bool _fssMode;

        // 当前编辑角色
        private static Studio.OCIChar _editChar;

        // 时间缩放
        private static float _timeScale = 1f;

        // ============ 场景浏览 ============

        private static string _sceneSubdir = ""; // 当前场景子目录(相对 SceneDir)

        /// <summary>当前场景目录:固定为配置 SceneDir(+子目录),保证 ◀▶ 能在全库场景间推进;
        /// 不跟随当前加载场景目录(否则子目录场景只有 1 张,无法推进)。</summary>
        private static string CurrentSceneDir()
        {
            string baseDir = ResolvePath(KKPEHeightLockPlugin.SceneDir.Value);
            if (string.IsNullOrEmpty(_sceneSubdir)) return baseDir;
            return System.IO.Path.Combine(baseDir, _sceneSubdir);
        }

        /// <summary>列出当前场景目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListSubdirs()
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                string dir = CurrentSceneDir();
                if (!System.IO.Directory.Exists(dir)) return result;
                foreach (var d in System.IO.Directory.GetDirectories(dir))
                    result.Add(System.IO.Path.GetFileName(d));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>进入子目录(多级:追加到当前子目录路径,支持子目录套子目录)。</summary>
        public static void SetSceneSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) return;
            _sceneSubdir = string.IsNullOrEmpty(_sceneSubdir) ? subdir : _sceneSubdir + "/" + subdir;
            RefreshSceneList();
        }

        /// <summary>返回上级目录。</summary>
        public static void GoParentDir()
        {
            try
            {
                if (string.IsNullOrEmpty(_sceneSubdir))
                {
                    // 已在根:跟随当前场景目录,回到配置根
                    return;
                }
                int idx = _sceneSubdir.LastIndexOf('\\');
                if (idx < 0) idx = _sceneSubdir.LastIndexOf('/');
                _sceneSubdir = idx >= 0 ? _sceneSubdir.Substring(0, idx) : "";
                RefreshSceneList();
            }
            catch (Exception) { }
        }

        /// <summary>子目录显示名。</summary>
        public static string SceneSubdirLabel()
        {
            return string.IsNullOrEmpty(_sceneSubdir) ? "根目录" : _sceneSubdir;
        }

        /// <summary>扫描场景目录(自然排序),目录跟随当前加载场景所在目录;无则用配置 SceneDir。</summary>
        public static void RefreshSceneList()
        {
            try
            {
                _sceneFiles.Clear();
                // 保留当前索引(不重置 -1),否则 ◀▶ 每次刷新后都回到头尾两张,无法推进
                string dir = CurrentSceneDir();
                if (!System.IO.Directory.Exists(dir)) return;

                // 递归扫描所有子目录的场景卡(用户场景常分散在分类子目录,只扫根目录会漏)
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.png", System.IO.SearchOption.AllDirectories))
                    _sceneFiles.Add(f);
                NaturalSort(_sceneFiles);
                _scenesLoaded = true;

                // 定位当前加载的场景(用 savePath 文件名);找不到则保留原索引(夹紧到合法范围)
                if (!string.IsNullOrEmpty(Studio.Studio.savePath))
                {
                    string cur = System.IO.Path.GetFileNameWithoutExtension(Studio.Studio.savePath);
                    if (!string.IsNullOrEmpty(cur))
                    {
                        for (int i = 0; i < _sceneFiles.Count; i++)
                        {
                            if (System.IO.Path.GetFileNameWithoutExtension(_sceneFiles[i]) == cur)
                            {
                                _sceneIndex = i;
                                break;
                            }
                        }
                    }
                }
                if (_sceneFiles.Count > 0)
                    _sceneIndex = Mathf.Clamp(_sceneIndex, 0, _sceneFiles.Count - 1);
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: found {_sceneFiles.Count} scenes in {dir}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RefreshSceneList error: " + e);
            }
        }

        /// <summary>自然排序(数字感知,同 sceneplayer.py 的 fns 排序)。</summary>
        private static void NaturalSort(System.Collections.Generic.List<string> files)
        {
            files.Sort(delegate(string a, string b)
            {
                return CompareNatural(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));
            });
        }

        private static int CompareNatural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i], cb = b[j];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int na = 0, nb = 0;
                    while (i < a.Length && char.IsDigit(a[i])) { na = na * 10 + (a[i] - '0'); i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { nb = nb * 10 + (b[j] - '0'); j++; }
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    if (ca != cb) return ca.CompareTo(cb);
                    i++; j++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>加载场景文件。</summary>
        public static void LoadScene(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
                // 换场景前自动关闭第一人称,避免视角锁在原地
                if (KKPEHeightLockPlugin.POVEnabled.Value)
                {
                    KKPEHeightLockPlugin.POVEnabled.Value = false;
                    KKPEHeightLockPlugin.Log.LogMessage("ScenePlayer: POV auto-disabled on scene switch");
                }
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null) return;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: loading {path}");
                studio.LoadScene(path);
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.LoadScene error: " + e);
            }
        }

        /// <summary>加载上一场景。</summary>
        public static void LoadPrevScene()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return;
            _sceneIndex = (_sceneIndex - 1 + _sceneFiles.Count) % _sceneFiles.Count;
            LoadScene(_sceneFiles[_sceneIndex]);
        }

        /// <summary>加载下一场景。</summary>
        public static void LoadNextScene()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return;
            _sceneIndex = (_sceneIndex + 1) % _sceneFiles.Count;
            LoadScene(_sceneFiles[_sceneIndex]);
        }

        /// <summary>删除当前场景文件(可选加载下一个)。</summary>
        public static void DeleteCurrentScene(bool loadNext)
        {
            try
            {
                if (_sceneIndex < 0 || _sceneIndex >= _sceneFiles.Count) return;
                string path = _sceneFiles[_sceneIndex];
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: deleted {path}");
                _sceneFiles.RemoveAt(_sceneIndex);
                if (loadNext && _sceneFiles.Count > 0)
                {
                    _sceneIndex = _sceneIndex % _sceneFiles.Count;
                    LoadScene(_sceneFiles[_sceneIndex]);
                }
                else if (_sceneFiles.Count == 0)
                {
                    _sceneIndex = -1;
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.DeleteCurrentScene error: " + e);
            }
        }

        private static void EnsureScenes()
        {
            if (!_scenesLoaded) RefreshSceneList();
        }

        // ============ 随机替换角色 ============

        private static string _rndFemaleSubdir = "";
        private static string _rndMaleSubdir = "";

        /// <summary>设置随机女卡目录子目录(多级追加;空串=回到根)。</summary>
        public static void SetRandomFemaleSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) { _rndFemaleSubdir = ""; return; }
            _rndFemaleSubdir = string.IsNullOrEmpty(_rndFemaleSubdir) ? subdir : _rndFemaleSubdir + "/" + subdir;
        }

        /// <summary>设置随机男卡目录子目录(多级追加;空串=回到根)。</summary>
        public static void SetRandomMaleSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) { _rndMaleSubdir = ""; return; }
            _rndMaleSubdir = string.IsNullOrEmpty(_rndMaleSubdir) ? subdir : _rndMaleSubdir + "/" + subdir;
        }

        /// <summary>随机女卡子目录显示名。</summary>
        public static string RandomFemaleSubdirLabel() { return string.IsNullOrEmpty(_rndFemaleSubdir) ? "女卡根" : _rndFemaleSubdir; }

        /// <summary>随机男卡子目录显示名。</summary>
        public static string RandomMaleSubdirLabel() { return string.IsNullOrEmpty(_rndMaleSubdir) ? "男卡根" : _rndMaleSubdir; }

        /// <summary>列出随机女卡目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListRandomFemaleSubdirs()
        {
            return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value, _rndFemaleSubdir);
        }

        /// <summary>列出随机男卡目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListRandomMaleSubdirs()
        {
            return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value, _rndMaleSubdir);
        }

        private static System.Collections.Generic.List<string> ListSubdirsOf(string baseDir, string subdir)
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                string dir = ResolvePath(baseDir);
                if (!string.IsNullOrEmpty(subdir))
                    dir = System.IO.Path.Combine(dir, subdir);
                if (!System.IO.Directory.Exists(dir)) return result;
                foreach (var d in System.IO.Directory.GetDirectories(dir))
                    result.Add(System.IO.Path.GetFileName(d));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>列出指定模式(0=场景 1=随机女 2=随机男 3=选卡)指定路径的直接子文件夹。路径用 "/" 分隔。</summary>
        public static System.Collections.Generic.List<string> ListSubdirsAt(int mode, string path)
        {
            switch (mode)
            {
                case 0: return ListSubdirsOf(KKPEHeightLockPlugin.SceneDir.Value, path);
                case 1: return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value, path);
                case 2: return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value, path);
                case 3:
                {
                    // 选卡目录:当前选卡窗口的根目录(UserData/chara/female 或 male)
                    string baseDir = System.IO.Path.Combine(UserData.Path, ConfigPanel.CardPickerBaseDir());
                    return ListSubdirsOf(baseDir, path);
                }
                default: return new System.Collections.Generic.List<string>();
            }
        }

        /// <summary>应用选择的子目录(0=场景 1=随机女 2=随机男 3=选卡),直接设为完整相对路径。</summary>
        public static void ApplySubdir(int mode, string path)
        {
            switch (mode)
            {
                case 0: _sceneSubdir = path ?? ""; RefreshSceneList(); break;
                case 1: _rndFemaleSubdir = path ?? ""; break;
                case 2: _rndMaleSubdir = path ?? ""; break;
                case 3: ConfigPanel.SetCardPickerDir(path ?? ""); break;
            }
        }

        /// <summary>随机替换所有角色(按性别)。</summary>
        public static void RandomReplaceAll()
        {
            RandomReplaceBySex(true, true);
        }

        /// <summary>随机替换女角色。</summary>
        public static void RandomReplaceFemale()
        {
            RandomReplaceBySex(true, false);
        }

        /// <summary>随机替换男角色。</summary>
        public static void RandomReplaceMale()
        {
            RandomReplaceBySex(false, true);
        }

        private static void RandomReplaceBySex(bool female, bool male)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;

                int replaced = 0;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oci = kv.Value as Studio.OCIChar;
                    if (oci == null || oci.charInfo == null) continue;
                    int sex = 0;
                    try { sex = oci.sex; } catch (Exception) { continue; }
                    if (!female && sex == 1) continue;
                    if (!male && sex == 0) continue;

                    string dir = sex == 1
                        ? (string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value)
                        : (string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value);
                    // 应用随机目录子目录
                    string rndSub = sex == 1 ? _rndFemaleSubdir : _rndMaleSubdir;
                    if (!string.IsNullOrEmpty(rndSub))
                        dir = System.IO.Path.Combine(dir, rndSub);
                    string card = PickRandomCard(dir);
                    if (card == null) continue;

                    // 保留身高
                    float origHeight = 0f;
                    if (KKPEHeightLockPlugin.RetainHeightOnRandom.Value)
                    {
                        try { origHeight = oci.charInfo.chaFile.custom.body.shapeValueBody[0]; } catch (Exception) { }
                    }

                    oci.ChangeChara(card);

                    if (KKPEHeightLockPlugin.RetainHeightOnRandom.Value && origHeight > 0f)
                    {
                        try { oci.charInfo.chaFile.custom.body.shapeValueBody[0] = origHeight; } catch (Exception) { }
                    }
                    replaced++;
                }
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: randomly replaced {replaced} character(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RandomReplaceBySex error: " + e);
            }
        }

        private static string PickRandomCard(string dir)
        {
            try
            {
                string path = ResolvePath(dir);
                if (!System.IO.Directory.Exists(path)) return null;
                var cards = System.IO.Directory.GetFiles(path, "*.png");
                if (cards.Length == 0) return null;
                var rng = new System.Random();
                return cards[rng.Next(cards.Length)];
            }
            catch (Exception) { return null; }
        }

        // ============ 角色快速编辑 ============

        /// <summary>设置当前编辑角色(取第一个选中角色)。</summary>
        public static void SetEditChar(Studio.OCIChar chara) { _editChar = chara; }

        /// <summary>设置全部服装状态(0=穿 1=半脱 2=脱)。
        /// 注意:不能用 SetClothesStateAll(会崩溃,sceneplayer 注释明确),改用逐部件 SetClothesState。</summary>
        public static void SetClothesAll(Studio.OCIChar chara, int state)
        {
            try
            {
                if (chara == null) return;
                for (int i = 0; i < 12; i++)
                    chara.SetClothesState(i, (byte)state);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>设置单个服装部件状态(0=穿 1=半脱 2=脱)。部件序:0上衣1下衣2内衣3内裤4泳衣上5泳衣下6泳衣上2 7泳衣下2 8裤袜9连裤袜10袜11鞋</summary>
        public static void SetClothesPart(Studio.OCIChar chara, int part, int state)
        {
            try { if (chara != null) chara.SetClothesState(part, (byte)state); }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>切换预设套装(CoordinateType:0校服1 1校服2 2体操 3泳装 4社团 5便服 6睡衣)。</summary>
        public static void SetCoordinateType(Studio.OCIChar chara, int type)
        {
            try
            {
                if (chara == null || chara.charInfo == null) return;
                var ct = (ChaFileDefine.CoordinateType)type;
                chara.charInfo.ChangeCoordinateTypeAndReload(ct);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>当前预设套装索引(-1=无)。</summary>
        public static int GetCoordinateType(Studio.OCIChar chara)
        {
            try
            {
                if (chara == null || chara.charInfo == null) return -1;
                return (int)chara.charInfo.fileStatus.coordinateType;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>切换 Kinematic 模式。</summary>
        public static void ToggleKinematic(Studio.OCIChar chara)
        {
            try
            {
                if (chara == null) return;
                chara.ActiveKinematicMode(Studio.OICharInfo.KinematicMode.None, false, true); // 先关
                // 简单切换:Anime → FK → IK → None
                chara.ActiveKinematicMode(Studio.OICharInfo.KinematicMode.FK, true, true);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>设置角色可见性。</summary>
        public static void SetVisible(Studio.OCIChar chara, bool visible)
        {
            try
            {
                if (chara == null) return;
                chara.SetVisibleSimple(visible);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        // ============ Timeline 控制 ============

        public static bool TimelinePlaying { get { try { return Timeline.Timeline.isPlaying; } catch (Exception) { return false; } } }
        public static float TimelineDuration { get { try { return Timeline.Timeline.duration; } catch (Exception) { return 0f; } } }
        public static float TimelineTime { get { try { return Timeline.Timeline.playbackTime; } catch (Exception) { return 0f; } } }

        public static void TimelinePlay() { try { Timeline.Timeline.Play(); } catch (Exception) { } }
        public static void TimelinePause() { try { Timeline.Timeline.Pause(); } catch (Exception) { } }
        public static void TimelineStop() { try { Timeline.Timeline.Stop(); } catch (Exception) { } }
        public static void TimelineSeek(float t) { try { Timeline.Timeline.Seek(t); } catch (Exception) { } }
        public static void TimelineSetTimeScale(float s)
        {
            _timeScale = s;
            try { UnityEngine.Time.timeScale = s; } catch (Exception) { }
        }
        public static float TimelineTimeScale { get { return _timeScale; } }

        // Timeline 相机插值项名(同 sceneplayer.py)
        private static readonly string[] TL_CAMERA_INTERPOLABLE_NAME =
        {
            "Camera Zoom", "Camera FOV", "Camera Position", "Camera Rotation",
            "Camera Origin Position", "Camera Origin Rotation"
        };

        private static System.Collections.Generic.List<Timeline.Interpolable> GetCameraInterpolables()
        {
            var result = new System.Collections.Generic.List<Timeline.Interpolable>();
            try
            {
                // false = 包含已禁用的插值项(否则 CAM 关闭后 enabled=false 就找不到了)
                var all = Timeline.Timeline.GetAllInterpolables(false);
                if (all == null) return result;
                foreach (var item in all)
                {
                    var ip = item as Timeline.Interpolable;
                    if (ip == null) continue;
                    foreach (var camName in TL_CAMERA_INTERPOLABLE_NAME)
                    {
                        if (ip.name == camName) { result.Add(ip); break; }
                    }
                }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>是否有 Timeline 相机关键帧。</summary>
        public static bool TimelineHasCameraKeyframe
        {
            get { return GetCameraInterpolables().Count > 0; }
        }

        /// <summary>Timeline 相机是否受控(任一相机插值项 enabled)。</summary>
        public static bool TimelineCameraControlled
        {
            get
            {
                foreach (var ip in GetCameraInterpolables())
                    if (ip.enabled) return true;
                return false;
            }
        }

        /// <summary>切换 Timeline 相机控制(同 sceneplayer.py tlToggleCamera)。</summary>
        public static void TimelineToggleCamera()
        {
            try
            {
                var list = GetCameraInterpolables();
                if (list.Count == 0) return;
                bool newState = true;
                foreach (var ip in list)
                    if (ip.enabled) { newState = false; break; }
                foreach (var ip in list)
                    ip.enabled = newState;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: timeline camera {(newState ? "controlled" : "free")}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.TimelineToggleCamera error: " + e);
            }
        }

        // ============ 场景人物选择(链接工作室选中)============

        /// <summary>获取当前场景所有角色(OCIChar 列表)。</summary>
        public static System.Collections.Generic.List<Studio.OCIChar> GetSceneCharacters()
        {
            return SceneReplacer.GetSceneCharacters();
        }

        /// <summary>在工作室场景树中选中指定角色(链接 Studio 原生选中)。</summary>
        public static void SelectCharacter(Studio.OCIChar chara)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.treeNodeCtrl == null || chara == null) return;
                foreach (var kv in studio.dicInfo)
                {
                    if (kv.Value == chara)
                    {
                        studio.treeNodeCtrl.SelectSingle(kv.Key, false);
                        KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: selected character {chara.charInfo?.fileParam?.fullname ?? "(unnamed)"}");
                        return;
                    }
                }
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer: character not found in scene tree");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.SelectCharacter error: " + e);
            }
        }

        /// <summary>角色是否当前被工作室选中。</summary>
        public static bool IsCharacterSelected(Studio.OCIChar chara)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.treeNodeCtrl == null || chara == null || studio.dicInfo == null) return false;
                var nodes = studio.treeNodeCtrl.selectNodes;
                if (nodes == null) return false;
                foreach (var n in nodes)
                {
                    Studio.ObjectCtrlInfo oci;
                    if (n != null && studio.dicInfo.TryGetValue(n, out oci) && oci == chara)
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // ============ FSS 文件夹子场景 ============

        /// <summary>收集 FSS 候选角色(同文件夹下唯一的角色,且不在根)。</summary>
        public static void RefreshFSS()
        {
            try
            {
                _fssChars.Clear();
                _fssIndex = -1;
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var c = kv.Value as Studio.OCIChar;
                    if (c != null && c.charInfo != null)
                        _fssChars.Add(c);
                }
                _fssMode = _fssChars.Count > 1;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: FSS candidates {_fssChars.Count}");
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RefreshFSS error: " + e); }
        }

        /// <summary>切换 FSS:只显示第 i 个角色,其余隐藏。</summary>
        public static void SwitchFSS(int index)
        {
            try
            {
                if (index < 0 || index >= _fssChars.Count) return;
                _fssIndex = index;
                for (int i = 0; i < _fssChars.Count; i++)
                {
                    if (_fssChars[i] != null)
                        SetVisible(_fssChars[i], i == index);
                }
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: FSS switched to {index + 1}/{_fssChars.Count}");
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.SwitchFSS error: " + e); }
        }

        public static int FssCount { get { return _fssChars.Count; } }
        public static int FssIndex { get { return _fssIndex; } }
        public static bool FssMode { get { return _fssMode; } }

        // ============ 工具 ============

        /// <summary>当前场景显示名。</summary>
        public static string SceneLabel()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return "(无场景)";
            int idx = _sceneIndex < 0 ? 0 : _sceneIndex;
            return $"{(idx + 1)}/{_sceneFiles.Count} {System.IO.Path.GetFileNameWithoutExtension(_sceneFiles[idx])}";
        }

        /// <summary>FSS 显示名。</summary>
        public static string FssLabel()
        {
            if (_fssChars.Count == 0) return "FSS: 无";
            return $"FSS: {_fssIndex + 1}/{_fssChars.Count}";
        }

        /// <summary>Timeline 显示名。</summary>
        public static string TimelineLabel()
        {
            try
            {
                float t = Timeline.Timeline.playbackTime;
                float d = Timeline.Timeline.duration;
                return string.Format("{0:0.0}/{1:0.0} {2}x", t, d, _timeScale);
            }
            catch (Exception) { return "Timeline 未加载"; }
        }

        /// <summary>当前编辑角色:实时取当前工作室选中的第一个角色(不缓存,换人立即生效)。</summary>
        public static Studio.OCIChar CurrentEditChar()
        {
            try
            {
                var selected = KKAPI.Studio.StudioAPI.GetSelectedCharacters();
                if (selected != null)
                {
                    foreach (var c in selected)
                    {
                        if (c != null && c.charInfo != null)
                            return c;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string ResolvePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith("UserData", StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(UserData.Path, p.Substring("UserData/".Length));
            return p;
        }
    }
}
