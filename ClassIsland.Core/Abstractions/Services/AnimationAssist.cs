namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// 全局动画与窗口行为开关。
/// </summary>
public static class AnimationAssist
{
    /// <summary>
    /// 动画等级
    /// </summary>
    public static int AnimationLevel { get; set; } = 1;

    /// <summary>
    /// 是否禁用过渡动画
    /// </summary>
    public static bool IsTransientDisabled { get; set; }

    /// <summary>
    /// 是否禁用动画等待。
    /// </summary>
    public static bool IsWaitForTransientDisabled { get; set; }

    /// <summary>
    /// 是否使用系统标题栏
    /// </summary>
    public static bool UseNativeTitlebar { get; set; }
}
