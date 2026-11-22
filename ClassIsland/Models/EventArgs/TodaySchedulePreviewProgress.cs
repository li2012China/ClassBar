namespace ClassIsland.Models.EventArgs;

/// <summary>
/// 「快速预览今日时间表」的进度信息。
/// </summary>
/// <param name="percent">0~1 的播放进度。</param>
/// <param name="text">用于展示的状态文本。</param>
public sealed class TodaySchedulePreviewProgress(double percent, string text)
{
    /// <summary>0~1 的播放进度。</summary>
    public double Percent { get; } = percent;

    /// <summary>用于展示的状态文本。</summary>
    public string Text { get; } = text;
}
