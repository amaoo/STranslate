## 更新

> **重要: 本版本起需要 .NET 10 Desktop Runtime**
> 软件为框架依赖发布, 升级后请先安装 [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0), 否则无法启动
> 如需回退, 请下载上一版本 1.7.x

- 升级: 目标框架 .NET 8 升级至 .NET 10 (`net10.0-windows`), 同步升级 Microsoft 相关依赖包至 10.0.15
- 升级: 第三方依赖包更新 (`Dapper` / `ScreenGrab` / `Serilog` / `Microsoft.CognitiveServices.Speech` 等)
- 移除: 三个已废弃的兼容包 (`System.Net.Http` / `System.Private.Uri` / `System.Text.RegularExpressions`), 改用框架内置实现
- 优化: 更新前置检查, 运行时版本低于 .NET 10 时引导安装, 避免更新后无法启动
- 修复: 文件读取未校验返回值 (`CA2022`)、SSE 流式读取同步阻塞 (`CA2024`) 两处隐患
- 新增: AWS Bedrock 翻译服务, 支持 API 密钥与 AccessKey/SecretKey 两种鉴权方式

> 该版本内部集成任务计划启动以跳过UAC弹窗确认, 用户可以在软件内设置是否启用管理员权限启动软件, 开机自启由软件内 `开机自启动` 控制
> **如果先前通过 `install_startup.bat` 创建了任务计划, 更新前请运行软件内 `uninstall_startup.bat` 删除旧的任务计划程序以避免异常情况**


## 离线数据

[123 网盘](https://www.123pan.com/s/AxlRjv-OuVmA.html)

> 离线数据包含: [简明英汉词典数据包](https://github.com/skywind3000/ECDICT/releases/download/1.0.28/ecdict-sqlite-28.zip)  [PaddleOCR数据包(v4.3)](https://github.com/ZGGSONG/STranslate/releases/download/0.01/stranslate_paddleocr_data_v4.3.zip) 等

> 另: OCR部分软件内置`微信OCR`(1.5.0.402版本开始内置微信OCR数据包，无需依赖外部微信)、词典部分软件内置了`必应`、`金山`词典，上述离线数据包配置较为繁琐，可直接使用软件内置服务