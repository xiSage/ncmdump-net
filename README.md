[![NuGet Version](https://img.shields.io/nuget/v/xiSage.LibNCM)](https://www.nuget.org/packages/xiSage.LibNCM)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/xiSage/ncmdump-net)

# ncmdump-net

基于 https://github.com/taurusxin/ncmdump 和 https://git.taurusxin.com/taurusxin/ncmdump-go 的 C# 移植版。

本仓库包含核心程序库以及基于核心库构建的控制台应用和GUI应用。

## GUI应用

![image](https://github.com/xiSage/ncmdump-net/blob/master/img/GUI.png)

## 控制台应用

处理单个或多个文件

```shell
ncmdump-net 1.ncm 2.ncm...
```

使用 `-d` 参数来指定一个文件夹，对文件夹下的所有以 ncm 为扩展名的文件进行批量处理

```shell
ncmdump-net -d source_dir
```

使用 `-r` 配合 `-d` 参数来递归处理文件夹下的所有以 ncm 为扩展名的文件

```shell
ncmdump-net -d source_dir -r
```

使用 `-o` 参数来指定输出目录，将转换后的文件输出到指定目录，该参数支持与 `-r` 参数一起使用

```shell
# 处理单个或多个文件并输出到指定目录
ncmdump-net 1.ncm 2.ncm -o output_dir

# 处理文件夹下的所有以 ncm 为扩展名并输出到指定目录，不包含子文件夹
ncmdump-net -d source_dir -o output_dir

# 递归处理文件夹并输出到指定目录，并保留目录结构
ncmdump-net -d source_dir -o output_dir -r
```
