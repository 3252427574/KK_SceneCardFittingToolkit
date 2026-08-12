# 场景卡套档工具箱 (Scene Card Fitting Toolkit)

Koikatu / Koikatsu 的 CharaStudio 插件：一键套档、替换角色、场景工具、VR 悬浮菜单。

## 功能

- **身高/身材锁定**：锁定 cf_n_height 身高缩放，不被姿势动画覆盖；替换角色时可保留体型
- **场景替换**：加载场景后按性别自动替换、随机替换（女/男/全部）、立即替换，可保留姿势/服装
- **场景播放器**：上/下一场景、加载/删除、场景子目录、FSS 子场景
- **Timeline**：播放/暂停/停止、CAM 相机跟随开关
- **服装预设**：校服1/2、体操、泳装、社团、便服、睡衣 一键切换 + 部件三态（穿/半脱/脱）
- **场景工具**：一键去无用球体、一键去马赛克（可设加载场景自动执行）
- **VR 第一人称**：相机跟随选中角色双眼，右手摇杆转头，参数可调
- **场景人物列表**：列出场景全部角色，一键在工作室中选中
- **VR 悬浮菜单**：完整复刻桌面菜单，分页浏览，VR 内可直接选角色卡/子目录，跟随眼前，带命中光标

## 安装

从仓库根目录下载 `KK_SceneCardFittingToolkit_v1.9.3.zip`，把其中 `BepInEx` 文件夹合并到游戏根目录（与 CharaStudio.exe 同级）。

仓库地址: https://github.com/3252427574/KK_SceneCardFittingToolkit

依赖：BepInEx 5、KKAPI、KKPE、Timeline、VRGIN_KKCS、KKCharaStudioVRPlugin(Ermin)。

## 使用

- 桌面：点工作室左侧工具栏图标（深红菱形）打开菜单
- VR：左手柄 Y 键呼出悬浮菜单；◀ ▶ 按钮翻页；右手射线指向按钮、扳机点击；命中点有青绿色圆环光标

## 构建

net35 项目（`.NET Framework` + Unity 5.6 引用，见 csproj 的 HintPath）。

```
cd 源码 && dotnet build -c Release
```

## 下载

最新插件包 `KK_SceneCardFittingToolkit_v1.9.3.zip` 在仓库根目录，直接下载即可。
