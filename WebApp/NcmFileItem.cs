using LibNCM;

namespace WebApp;

public class NcmFileItem
{
    public string FileName { get; }
    public string OutputFileName { get; private set; }
    public NcmStatus Status { get; private set; }
    public string Message { get; private set; }
    public byte[]? Data { get; private set; }
    public byte[]? RawData { get; private set; }
    public NeteaseCloudMusicMetadata? Metadata { get; private set; }

    public enum NcmStatus { Waiting, Processing, Finished, Failed }

    public NcmFileItem(string fileName, byte[]? rawData = null)
    {
        FileName = fileName;
        OutputFileName = fileName;
        Status = NcmStatus.Waiting;
        Message = "等待处理";
        RawData = rawData;
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
        Status = NcmStatus.Waiting;
        Message = "等待处理";
        Data = null;
        Metadata = null;
    }
}
