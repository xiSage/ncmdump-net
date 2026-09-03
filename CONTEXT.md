# 领域词汇表 · ncmdump-net

> 单上下文仓库。术语为本仓库代码与讨论使用的规范语言；**不要**在代码/issue/评审中使用规避词（见各条）。

## NCM 文件（.ncm）
网易云音乐加密音频容器。结构：`CTENFDAM` 魔数 → 2 字节间隙 → 密钥段 → 元数据段 → 封面帧段 → 音频负载（`Aud` 前缀标记）。整段不计魔数的内容都经过 `0x64`/`0x63` XOR 与 AES 层处理。是**输入**侧的核心概念。_规避词_：「文件」「加密文件」（太宽）。

## NCM 头（NCM header）
NCM 文件的解析结果：容器格式（`NcmFormat`）、歌曲元数据（`NeteaseCloudMusicMetadata`）、内嵌封面图、远程封面 URL、音频负载偏移。由 `NcmHeaderParser` 一次性解析产物,存于 `NcmHeader`。_规避词_：「头部数据」「header 信息」。

## 密钥段 / 元数据段 / 封面帧段
- **密钥段**：`0x64` XOR 后的 AES-128-ECB 密文,解密后以 `neteasecloudmusic` 前缀开头,其后是派生密钥盒的字节。
- **元数据段**：`0x63` XOR 后 Base64,AES 解密后以 `music:` 前缀开头,内容是 JSON 元数据。
- **封面帧段**：长度对（帧长/数据长）+ JPEG 图数据。三个都是 `NcmHeaderParser` 的解析单元。

## 密钥盒（KeyBox）
从密钥段派生的 256 字节置换状态,是音频负载解密的唯一依据。**库内概念,不出现在公开接口**（`NcmHeader.KeyBox` 为 internal）。_规避词_：「key」「密钥」（歧义：指密钥段还是密钥盒——用「密钥段」与「密钥盒」区分）。

## 解密流（decrypting stream）
按负载相对位置对音频字节做密钥盒 XOR 的只读流（`NcmAudioStream`,internal）。读取即解密,位置从音频负载起点 0 计。_规避词_：「解密后的流」。

## 门面（facade）
`NcmFile`：公开的唯一入口。`Open`（路径/流）完成`NcmHeaderParser.Parse`;之后 `DumpToBytesAsync` / `DumpToFileAsync` / `FixMetadataAsync` 全部幂等、带取消令牌、无隐式状态跳变。三端消费者（CLI/GUI/Web）只与门面打交道。_规避词_：「服务」（会与 `NcmDecryptService` 等 Web 端薄适配器混淆）。

## 封面提供器（cover provider）
远程封面获取的 seam（ADR-0001 曾记为已知负债,现已实施）：`ICoverArtProvider` 接口 + `RemoteCoverArtProvider` 默认 HTTP 实现;`NcmFile.Open(path|stream, coverArtProvider?)` 可选注入,测试注入 fake——两个适配器 = 真 seam。

## 处理编排（processing orchestration）
"打开→转储→写元数据→汇报结果"的完整流程,现为库内深门面 `NcmProcessor`（`ProcessAsync` 文件目标 / `ProcessToBytesAsync` 内存目标):CLI/GUI/Web 各自只剩一行调用与界面表达。错误模型:致命失败 → `Success=false` + `ErrorMessage`;元数据写失败**非致命** → `MetadataWarning`。

## 消费者
- **CLI**（ConsoleApp）：命令行批处理,`async` 调用门面,打印进度。
- **GUI**（DesktopApp）：Avalonia MVVM,`FileItem.Process` 驱动状态机,并发 4。
- **Web**（WebApp）：Blazor WASM,`NcmDecryptService` 内存字节适配,不抓远程封面。

## 相关决策
见 `docs/adr/0001-split-neteasecloudmusicstream.md`（破坏性 2.0：拆 God 类为深零件 + 门面）。