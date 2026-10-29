# LXLPCL

轻量的 Minecraft 启动器（WPF / .NET 8），界面参考 PCL2，单文件绿色运行。

- 当前版本：**1.0.0**
- 作者：楼小乐
- 问题反馈 Q 群：**416980382**

## 功能

- 版本安装与管理（原版 / Fabric / Forge / NeoForge 等），自动下载补全资源与 Java
- 微软正版、外置登录、离线账户
- Modrinth 模组下载，模组介绍自动翻译中文，模组详情一键跳转
- 资源与 Mod 管理、内存与 JVM 参数配置（主界面可直接设置内存）
- 皮肤编辑器：图层系统、3D 模型预览、行走模拟（WASD / 空格跳跃）、我的皮肤库
- 联机功能：局域网 / 樱花映射（natfrp）一键连接
- 汉化：Mod 汉化 / 整合包汉化，对比原文与中文，补全缺失条目并生成资源包补丁
- **自动更新**：启动时检查 GitHub Release，发现新版本可一键下载整包替换并自动重启

## 下载

最新版本见 [Releases](https://github.com/louxiaole/LXLPCL/releases)。

## 自动更新机制

启动器启动后会请求 GitHub Releases 最新版本（失败时回退到仓库根目录的 version.json），
比较版本号后弹出更新窗口；确认更新会下载 `LXLPCL-vX.Y.Z-win64.zip`，
由生成的批处理脚本在退出后解压覆盖并重启，同时自动备份/恢复配置文件。

发版流程：修改 `LXLPCL/LXLPCL.csproj` 中的 `<Version>`，构建 zip 后创建同 tag 的 Release 并上传，
再同步更新根目录 version.json。

## 自行编译

```bash
dotnet publish LXLPCL/LXLPCL.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```
