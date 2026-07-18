# 自动化测试说明

## 在 Visual Studio 中运行

1. 打开 `IndustrialVisionHost.sln`。
2. 选择菜单“测试 → 测试资源管理器”。
3. 点击“运行所有测试”。

## 在命令行运行

关闭正在运行的上位机后，在项目根目录执行：

```powershell
dotnet test IndustrialVisionHost.sln -c Debug
```

如果上位机界面正在运行并锁定主程序，可以复用已经生成的主程序 DLL，只构建测试工程：

```powershell
dotnet test tests\IndustrialVisionHost.Tests\IndustrialVisionHost.Tests.csproj -c Debug -p:BuildProjectReferences=false --no-restore
```

只运行稳定性与性能基线：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-stability-tests.ps1
```

## 当前测试范围

- 三种角色的权限矩阵
- 手动/自动模式与触发来源矩阵
- 设备状态机正常周期、非法跳转、急停与事件参数
- 系统设置默认创建、保存恢复、旧文件兼容与非法值拒绝
- 报警重复合并、确认权限、恢复清除与再次发生的新生命周期
- 文本PLC真实回环连接、PING/PONG和完整START/BUSY/RESULT握手
- 心跳成功、服务端停止后的断线识别和自动重连恢复
- Modbus TCP线圈/保持寄存器真实读写及协议异常响应
- 检测历史SQLite保存、筛选、分页、统计和旧库迁移
- 模拟相机打开/关闭、标准帧、非法配置和采集断线场景
- 标准、双目标、小目标、移动目标和噪声目标的视觉检测
- 数量、面积、宽度、高度、ROI和形态学判定分支
- 灰度、二值、形态学调试图尺寸与通道数
- 循环视觉处理后的OpenCV `Mat`释放契约
- 相机采集服务启动、首帧、最近帧克隆、停止和释放生命周期
- 多线程并发启动只能创建一个采集循环，并发停止保持幂等
- 采集异常后的重连成功、重连耗尽和失败事件次数
- 停止操作立即取消长时间重连等待，不阻塞窗口关闭

测试使用独立临时目录和临时 SQLite 数据库，不会修改正式生产数据。
