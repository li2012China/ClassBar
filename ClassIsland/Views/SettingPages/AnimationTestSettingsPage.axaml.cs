using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Models.EventArgs;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// 测试动画设置页：手动播放主界面各类事件的动画。
/// </summary>
[SettingsPageInfo("animation-test", "测试动画", "\ue920", "\ue920", SettingsPageCategory.Internal)]
public partial class AnimationTestSettingsPage : SettingsPageBase
{
    private const string PreviewHeader = "快速预览今日时间表";
    private const string PreviewDescription =
        "在 10 秒内快进播放今日时间表的全部内容（上课、课间、放学），相当于快速检查主界面一整天的显示与动画效果；预览期间会暂时关闭提醒。";

    private bool _isPreviewing;

    public AnimationTestSettingsPage()
    {
        InitializeComponent();
        Unloaded += (s, a) =>
        {
            if (_isPreviewing)
            {
                MainWindow?.StopTodaySchedulePreview();
            }
        };
    }

    private static MainWindow? MainWindow => AppBase.Current.MainWindow as MainWindow;

    private async void ButtonPlayEntrance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (MainWindow is { } window)
        {
            await window.PlayEntranceAnimationForTestAsync();
        }
    }

    private async void ButtonPlayCollapse_OnClick(object? sender, RoutedEventArgs e)
    {
        if (MainWindow is { } window)
        {
            await window.PlayCollapseAnimationForTestAsync();
        }
    }

    private async void ButtonShowWelcome_OnClick(object? sender, RoutedEventArgs e)
    {
        if (MainWindow is { } window)
        {
            await window.ShowWelcomeForTestAsync();
        }
    }

    private async void ButtonPlayTodaySchedule_OnClick(object? sender, RoutedEventArgs e)
    {
        if (MainWindow is not { } window)
        {
            return;
        }
        if (_isPreviewing)
        {
            window.StopTodaySchedulePreview();
            return;
        }

        _isPreviewing = true;
        PreviewStatusPanel.IsVisible = true;
        PreviewStopButton.IsVisible = true;
        PreviewStatusText.Text = "正在准备今日时间表…";
        PreviewProgressBar.Value = 0;
        PreviewExpander.Header = "停止快速预览";
        PreviewExpander.Description = "正在 10 秒内快进播放今日时间表，点击可提前停止。";

        var progress = new Progress<TodaySchedulePreviewProgress>(OnPreviewProgress);
        try
        {
            var error = await window.PlayTodaySchedulePreviewAsync(progress);
            if (!string.IsNullOrEmpty(error))
            {
                PreviewStatusText.Text = error;
                PreviewProgressBar.Value = 0;
            }
        }
        finally
        {
            _isPreviewing = false;
            PreviewExpander.Header = PreviewHeader;
            PreviewExpander.Description = PreviewDescription;
            PreviewStopButton.IsVisible = false;
        }
    }

    private void OnPreviewProgress(TodaySchedulePreviewProgress p)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPreviewProgress(p));
            return;
        }
        PreviewProgressBar.Value = p.Percent;
        PreviewStatusText.Text = p.Text;
    }

    private void ButtonStopTodaySchedule_OnClick(object? sender, RoutedEventArgs e)
    {
        MainWindow?.StopTodaySchedulePreview();
    }
}
