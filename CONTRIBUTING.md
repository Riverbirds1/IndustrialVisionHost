# 贡献说明

感谢关注工业视觉检测上位机。本仓库当前主要用于上位机开发学习、工程实践和作品展示。

## 开发环境

- 64位Windows 10或Windows 11
- Visual Studio 2022与“.NET桌面开发”工作负载
- .NET 6 SDK

## 开发流程

1. 从`main`分支创建功能或修复分支。
2. 一次提交只处理一个明确问题，避免同时混入无关格式化。
3. 修改业务逻辑时补充或更新相应xUnit测试。
4. 提交前完成Debug、Release构建和Release测试。
5. 不提交数据库、日志、NG图片、发布目录、Visual Studio个人配置或真实设备凭据。

建议的本地检查命令：

```powershell
dotnet build IndustrialVisionHost.sln -c Debug
dotnet build IndustrialVisionHost.sln -c Release
dotnet test tests\IndustrialVisionHost.Tests\IndustrialVisionHost.Tests.csproj -c Release
```

## 提交消息

推荐使用简短的约定式前缀：

- `feat:` 新功能
- `fix:` 错误修复
- `test:` 测试补充
- `docs:` 文档修改
- `refactor:` 不改变功能的结构调整
- `build:` 构建、依赖或发布配置

示例：

```text
fix: prevent duplicate camera acquisition loops
docs: add Modbus register map
```

## 问题反馈

提交问题时请包含：

- 操作系统、.NET SDK和程序版本
- 可重复的操作步骤
- 预期结果与实际结果
- 相关运行日志或异常堆栈
- 是否使用模拟器还是真实设备

日志或截图中如包含用户名、设备IP、产线名称、产品信息和本地路径，请先脱敏。

## 真实设备代码

接入工业相机或PLC时，不要提交厂商许可证、私有SDK安装包、真实IP、账号密码或客户协议文档。设备适配层应继续遵循现有接口边界，并保留模拟实现用于自动化测试。
