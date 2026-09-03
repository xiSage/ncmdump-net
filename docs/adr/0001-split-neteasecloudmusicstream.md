# ADR-0001: 拆分 NeteaseCloudMusicStream 为深零件 + 门面（破坏性 2.0）

- 状态：已接受（2026-09-04）
- 影响范围：LibNCM（NuGet 包 `xiSage.LibNCM`）
- 关联：本仓库领域词汇见 `CONTEXT.md`

## 背景

`NeteaseCloudMusicStream`（LibNCM 1.x）是 637 行的"上帝类",同时承担七种职责：NCM 头解析、解密流、输出流管理、内存缓冲、TagLib `IFileAbstraction` 适配、元数据写入、远程封面下载。它继承 `Stream` 暴露约 20 个虚成员,接口包含隐式顺序约束（`DumpToFile` 后 `Position` 语义切换;写操作隐式触发 `DumpToMemory`;重复 `DumpToFile` 泄漏句柄）。三种底层状态（原始流/输出文件流/内存缓冲）让 `Read/Write/Position/Length/Seek/SetLength` 全部长成三重分支。构造函数即解析,异常时已打开的 `FileStream` 只能靠 GC 兜底；`SharedHttpClient` 为私有静态,不可注入。

后果(由独立复核确认)：三端消费者(CLI/GUI/Web)各自复制"打开→解密→写元数据→汇报"编排；Web 端因接口不适配自建 32KB 读取循环平铺拷贝,并重复实现 TagLib 元数据写入(`ApplyMetadata`)与 `IFileAbstraction`,语义已与库内 `FixMetadataAsync` 漂移(漏 `Description`、不抓封面、错误语义相反)；加密/格式库零测试。

## 决策

采用**破坏性变更**,LibNCM 升 **2.0**。公开面从"上帝流"缩小为：

```
公开
├── NcmFile            静态工厂 Open(path|stream);构造私有;Open 即解析一次
│     ├── DumpToBytesAsync(ct)   内存字节,幂等
│     ├── DumpToFileAsync(dir, name, ct)   扩展名门面推导,幂等
│     ├── FixMetadataAsync(fetchCoverArt, ct)   TagLib 写,内存/磁盘双路径
│     └── Format / Metadata / ImageData / AlbumPicUrl / FormatExtension / OutputFileNameFor
├── NcmHeaderParser     Parse(Stream) → NcmHeader (纯解析,流停在音频负载起点)
├── NcmHeader           Format / Metadata / ImageData / AlbumPicUrl(不含密钥盒)
└── NcmException 层次   原样保留
internal (InternalsVisibleTo → Ncm.Tests)
├── NcmAudioStream : Stream  只读解密流,不拥有底层流
├── KeyBoxCipher             位置相关 XOR 共享实现
└── 密钥盒                  经内部组装传递,不出现在公开接口
```

原则与本决策绑定：
- **接口即测试面**：解析器可喂合成字节直测;解密流经 `InternalsVisibleTo` 直达;门面是完整管线测试的入口。
- **消灭隐式状态跳变**：输出方法幂等、顺序自由;重复 dump 不泄漏句柄;构造失败不产生半成品实例。取消令牌贯穿所有异步方法(含远程封面下载)——修复 1.x"取消不穿透"。
- **只出异步**：CLI 改 `async Task Main`,同步重载删除(无同步消费者,通过删除测试)。
- **分期**：本 ADR 只落地零件拆分与门面；元数据写入深化(`NcmMetadataWriter` + `ICoverArtProvider` seam)与编排门面(`NcmProcessor`)分别留待候选 3、候选 2。

## 明确不做（本决策范围外）

- 静态 `HttpClient` 封面下载本轮保持原状（已知负债,记入 CONTEXT.md,候选 3 处理）。
- 编排(处理一个文件的流程)仍在三端各写(各 3–5 行门面调用),不上移(候选 2)。
- 三份逐字节相同的 `Roots.xml` trimmer 根与 nupkg 内容漂移(包不含 Roots.xml、仅 net9.0)不在此次修复,另行处理。

## 后果

- 正面：公开面变小、顺序约束消失、三端各减约 50 行平铺实现、Web 端重复的 `ApplyMetadata`/`MemoryStreamFileAbstraction` 删除、加密/解析/门面获得 32 个单元测试(NIST AES 向量 + 合成夹具)护栏。
- 负面：破坏性升级——NuGet 消费者需迁移到 `NcmFile`;`NeteaseCloudMusicStream` 及其公开成员被删除。
- 验证：`Ncm.Tests` 32/32 通过(2026-09-04);CLI/GUI/Web 均编译通过。

## 备选方案

- 非破坏(保留旧类标 `[Obsolete]` 做薄门面)：否决——外部消费者不可枚举,与其长期维护两层接口不如一次迁移;仓库三端为源码引用,迁移成本已付。
- 一步到位拆出全部角色(含元数据写入器与封面 seam)：推迟——范围爆炸,元数据深化与封面解耦属于独立候选。