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

    public enum NcmStatus { Waiting, Processing, Finished, Failed }

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
        Status = NcmStatus.Waiting;
        Message = "等待处理";
        Data = null;
        Metadata = null;
    }
}