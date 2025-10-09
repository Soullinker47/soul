# ExocadDailyExporter

ExocadDailyExporter 是一个基于 .NET 8 的 Windows 托盘常驻小程序，用于自动收集 exocad 病例目录中的制造用文件，并每天归档到指定目录，方便与加工端共享。

## 核心能力

- 首次启动引导设置归档目录，可立即执行“今天全量扫描归档”。
- 双击即运行，无需安装，支持单实例常驻托盘。
- 实时监听病例目录内的白名单文件更新，自动归档到 `归档根目录\年\日期\病例名` 结构。
- 内置白名单严格匹配制造 STL 与处方 XML，显式排除预览图、截图、崩溃报告等文件。
- 计划任务（`schtasks`）按每日固定时间触发全量扫描；若计划任务不可用，则自动退回“启动文件夹”自启并给出提示。
- 开机自启开关、稳定等待秒数、日志保留天数、每日全量时间等都可在设置界面修改并立即生效。
- 日志按天滚动保存于 `logs\archive-YYYYMMDD.log`。

## 快速上手

1. 下载发布包并解压，双击 `ExocadDailyExporter.exe`。
2. 首次启动会弹出“设置归档路径”对话框，选择归档目录后点击“开始运行”即可完成初始化并执行一次全量归档。
3. 程序将最小化到托盘，右键图标可打开“设置…”、“立刻导入今天”、“打开归档文件夹”、“打开日志文件夹”或退出程序。
4. 在设置窗口内可调整源目录、归档目录、稳定等待秒数、每日全量时间、日志保留天数及开机自启，并查看实时日志。

## 白名单规则

仅复制以下文件（大小写不敏感）：

- 制造 STL：`*_crown_cad.stl`、`*_fullcontour_cad.stl`、`*_reduced_cad.stl`、`*_coping_cad.stl`、`*_veneer_cad.stl`、`*_inlay_cad.stl`、`*_onlay_cad.stl`、`*_overlay_cad.stl`、`*bridge*_cad.stl`、`*_pontic_cad.stl`、`*_abutment_cad.stl`、`*_meso_cad.stl`、`*_crown_implant_cad.stl`、`*_sleeve_cad.stl`、`*_tibase_cad.stl`、`*_bar_cad.stl`、`*_provisional_cad.stl`、`*_temp_cad.stl`、`*_longterm_temp_cad.stl`、`*_splint_cad.stl`、`*_bite_splint_cad.stl`、`*_setup_cad.stl`、`*_tryin_cad.stl`、`*_base_cad.stl`、`*_denture_cad.stl`、`*_gingiva_cad.stl`、`*_primary_telescopic_cad.stl`、`*_secondary_telescopic_cad.stl`、`*_post_cad.stl`、`*_core_cad.stl`、`*_rpd_cad.stl`、`*_framework_cad.stl`、`*_ramp_cad.stl`。
- 处方 XML：`*.constructionInfo`

显式排除：`crashreport*`、`*preview*.stl`、`*screenshot*.png`。

## 构建与发布

### 本地发布

执行项目根目录中的 `build.ps1`，脚本会完成以下工作：

1. `dotnet restore`
2. `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/win-x64`
3. 打包 `dist/win-x64` 目录下的 `ExocadDailyExporter.exe`、`appsettings.json`、`README.md` 为 `dist/ExocadDailyExporter-win-x64.zip`
4. 生成 `dist/download-link.txt`，包含本地压缩包的 `file://` 下载链接

### GitHub Actions

`.github/workflows/release.yml` 提供了手动/打标签触发的发布流程，会自动构建压缩包并上传到 Release，同步在 Job Summary 中输出 Release 页面与直链地址。

## 单元测试

项目使用 xUnit 覆盖白名单匹配、排除策略、覆盖策略以及当日变更判定逻辑：

```bash
 dotnet test
```

## 注意事项

- 程序不需要管理员权限即可运行；如计划任务创建失败将自动回落到“启动文件夹”自启并在状态栏提示原因。
- 请确保源目录和归档目录均位于本地磁盘或高速网络盘，避免频繁 IO 错误。
- 稳定等待秒数用于避免复制未写完的文件，建议保持默认 30 秒，根据设备性能适度调整。
