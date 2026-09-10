using WebApp;
using Xunit;

namespace Ncm.Tests;

/// <summary>State machine behind one row of the web app's file list.</summary>
public class NcmFileItemTests
{
    [Fact]
    public void NewItemWaitsForTheBytesToArrive()
    {
        var item = new NcmFileItem("song.ncm");

        Assert.Equal(NcmFileItem.NcmStatus.Waiting, item.Status);
        Assert.Null(item.RawData);
    }

    [Fact]
    public void ReadingReportsPercentageProgress()
    {
        var item = new NcmFileItem("song.ncm");

        item.SetReading(0, 1000);
        Assert.Equal(NcmFileItem.NcmStatus.Reading, item.Status);
        Assert.Equal("读取文件 0%", item.Message);

        item.SetReading(450, 1000);
        Assert.Equal("读取文件 45%", item.Message);

        item.SetReading(1000, 1000);
        Assert.Equal("读取文件 100%", item.Message);
    }

    [Fact]
    public void ReadingOfAnEmptyFileDoesNotDivideByZero()
    {
        var item = new NcmFileItem("empty.ncm");

        item.SetReading(0, 0);

        Assert.Equal(NcmFileItem.NcmStatus.Reading, item.Status);
        Assert.Equal("读取文件", item.Message);
    }

    [Fact]
    public void DataMakesTheItemProcessable()
    {
        var item = new NcmFileItem("song.ncm");
        item.SetReading(0, 3);

        item.SetData([1, 2, 3]);

        Assert.Equal(NcmFileItem.NcmStatus.Waiting, item.Status);
        Assert.Equal("等待处理", item.Message);
        Assert.Equal([1, 2, 3], item.RawData);
    }

    [Fact]
    public void ReadFailureIsTerminalUntilReset()
    {
        var item = new NcmFileItem("song.ncm");
        item.SetReading(0, 10);

        item.SetFailed("读取文件失败: 传输中断");

        Assert.Equal(NcmFileItem.NcmStatus.Failed, item.Status);
        Assert.Equal("读取文件失败: 传输中断", item.Message);
        Assert.Null(item.RawData);
    }

    [Fact]
    public void ResetKeepsTheBytesSoARetriedFileIsNotReRead()
    {
        var item = new NcmFileItem("song.ncm", [1, 2, 3]);
        item.SetProcessing();
        item.SetFailed("boom");

        item.Reset();

        Assert.Equal(NcmFileItem.NcmStatus.Waiting, item.Status);
        Assert.Equal([1, 2, 3], item.RawData);
        Assert.Null(item.Data);
    }

    [Fact]
    public void FinishedCarriesTheOutputNameAndMetadata()
    {
        var item = new NcmFileItem("song.ncm");

        item.SetFinished([9, 9], "song.flac");

        Assert.Equal(NcmFileItem.NcmStatus.Finished, item.Status);
        Assert.Equal("song.flac", item.OutputFileName);
        Assert.Equal([9, 9], item.Data);
    }
}