using LibNCM;

namespace WebApp;

public class NcmFileItem(string fileName, byte[]? rawData = null)
{
    public string FileName { get; } = fileName;
    public string OutputFileName { get; private set; } = fileName;
    public NcmStatus Status { get; private set; } = NcmStatus.Waiting;
    public string Message { get; private set; } = "等待处理";
    public byte[]? Data { get; private set; }
    public byte[]? RawData { get; private set; } = rawData;
    public NeteaseCloudMusicMetadata? Metadata { get; private set; }

    public enum NcmStatus { Waiting, Reading, Processing, Finished, Failed }

    /// <summary>
    ///   Reports progress while the browser file is still being copied into memory, so a slow
    ///   read shows up as a live entry instead of nothing at all.
    /// </summary>
    public void SetReading(long readBytes, long totalBytes)
    {
        Status = NcmStatus.Reading;
        Message = totalBytes > 0
            ? $"读取文件 {readBytes * 100 / totalBytes}%"
            : "读取文件";
    }

    /// <summary>Attaches the bytes read from the browser and marks the item ready to process.</summary>
    public void SetData(byte[] data)
    {
        RawData = data;
        SetWaiting();
    }

    public void SetProcessing()
    {
        Status = NcmStatus.Processing;
        Message = "正在处理";
    }

    public void SetFinished(byte[] data, string outputFileName, NeteaseCloudMusicMetadata? metadata = null)
    {
        Status = NcmStatus.Finished;
        Data = data;
        OutputFileName = outputFileName;
        Metadata = metadata;
        Message = "处理完成";
    }

    public void SetFailed(string error)
    {
        Status = NcmStatus.Failed;
        Message = error;
    }

    public void Reset()
    {
        SetWaiting();
        Data = null;
        Metadata = null;
    }

    private void SetWaiting()
    {
        Status = NcmStatus.Waiting;
        Message = "等待处理";
    }
}