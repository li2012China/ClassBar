using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Core;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Controls.EditMode;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Extensions.UI;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Helpers;
using ClassIsland.Models.EventArgs;
using ClassIsland.Platforms.Abstraction;
using ClassIsland.Platforms.Abstraction.Enums;
using ClassIsland.Platforms.Abstraction.Models;
using ClassIsland.Shared.Abstraction.Models;
using ClassIsland.Shared.Abstraction.Services;
using ClassIsland.Shared.Interfaces;
using ClassIsland.Shared.Models.Notification;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Interfaces.Controls;
using ClassIsland.ViewModels;
using ClassIsland.Views;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using Sentry;
using Linearstar.Windows.RawInput;
using YamlDotNet.Core;



#if DEBUG
using JetBrains.Profiler.Api;
#endif

namespace ClassIsland;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[PseudoClasses(":dock-top", ":dock-bottom", ":windowed", ":no-windowed-transparent")]
public partial class MainWindow : Window, ITopmostEffectPlayer
{
    #region Fields & Properties
    // public static readonly ICommand TrayIconLeftClickedCommand = new RoutedCommand();

    public event EventHandler? StartupCompleted;

    public MainViewModel ViewModel
    {
        get;
        set;
    }

    // private Storyboard NotificationProgressBar { get; set; } = new Storyboard();

    private SettingsService SettingsService
    {
        get;
    }

    private ITaskBarIconService TaskBarIconService
    {
        get;
    }

    public INotificationHostService NotificationHostService
    {
        get;
    }

    public IProfileService ProfileService
    {
        get;
    }

    public ILessonsService LessonsService { get; }

    // public TopmostEffectWindow TopmostEffectWindow { get; }

    private bool IsRunningCompatibleMode { get; set; } = false;

    private Stopwatch UserPrefrenceUpdateStopwatch
    {
        get;
    } = new();

    private IExactTimeService ExactTimeService { get; }

    public ISpeechService SpeechService { get; }

    public IComponentsService ComponentsService { get; }

    private ILogger<MainWindow> Logger;

    private double _latestDpiX = 1.0;
    private double _latestDpiY = 1.0;


    private DispatcherTimer HighFreqTopmostRecheckTimer { get; } = new()
    {
        Interval = TimeSpan.FromMilliseconds(1)
    };

    private DispatcherTimer TouchInFadingTimer { get; set; } = new();

    private Stopwatch RawInputUpdateStopWatch { get; } = new();

    private ClassChangingWindow? ClassChangingWindow { get; set; }
    
    private IUriNavigationService UriNavigationService { get; }
    public IRulesetService RulesetService { get; }
    public IWindowRuleService WindowRuleService { get; }
    public IManagementService ManagementService { get; }
    public TopmostEffectWindow TopmostEffectWindow { get; }
    
    public event EventHandler<MousePosChangedEventArgs>? MousePosChanged;

    public event EventHandler<RawInputEventArgs>? RawInputEvent;

    public event EventHandler<MainWindowAnimationEventArgs>? MainWindowAnimationEvent;

    private Point _centerPointCache = new Point(0, 0);

    private List<object> TopmostLocks { get; } = [];


    public static readonly StyledProperty<double> BackgroundWidthProperty = AvaloniaProperty.Register<MainWindow, double>(
        nameof(BackgroundWidth));

    public double BackgroundWidth
    {
        get => GetValue(BackgroundWidthProperty);
        set => SetValue(BackgroundWidthProperty, value);
    }

    public static readonly StyledProperty<double> NotificationProgressBarValueProperty = AvaloniaProperty.Register<MainWindow, double>(
        nameof(NotificationProgressBarValue));
    public double NotificationProgressBarValue
    {
        get => GetValue(NotificationProgressBarValueProperty);
        set => SetValue(NotificationProgressBarValueProperty, value);
    
    }

    public const string DefaultFontFamilyKey =
        "avares://ClassIsland/Assets/Fonts/#HarmonyOS Sans SC, avares://ClassIsland.Core/Assets/Fonts/#FluentSystemIcons-Resizable";
    
    public static FontFamily DefaultFontFamily { get; }=
        FontFamily.Parse(DefaultFontFamilyKey);

    public NativeMenu MoreOptionsMenu { get; } = [];

    #endregion

    #region Initialization
    public MainWindow(SettingsService settingsService, 
        IProfileService profileService,
        INotificationHostService notificationHostService, 
        ITaskBarIconService taskBarIconService,
        ILogger<MainWindow> logger, 
        ISpeechService speechService,
        IExactTimeService exactTimeService,
        IComponentsService componentsService,
        ILessonsService lessonsService,
        IUriNavigationService uriNavigationService,
        IRulesetService rulesetService,
        IWindowRuleService windowRuleService,
        IManagementService managementService,
        TopmostEffectWindow topmostEffectWindow)
    {
        Logger = logger;
        SpeechService = speechService;
        SettingsService = settingsService;
        TaskBarIconService = taskBarIconService;
        NotificationHostService = notificationHostService;
        ProfileService = profileService;
        ExactTimeService = exactTimeService;
        ComponentsService = componentsService;
        LessonsService = lessonsService;
        UriNavigationService = uriNavigationService;
        RulesetService = rulesetService;
        WindowRuleService = windowRuleService;
        ManagementService = managementService;
        TopmostEffectWindow = topmostEffectWindow;
        
        ViewModel = new MainViewModel();
        DataContext = this;
        InitializeComponent();
        ProfileTabContentHost.Content = App.GetService<ProfileSettingsWindow>();

        // 防闪现：构造时先把主界面内容移出屏幕，等入场动画开始后再滑入。
        ContentSlideTransform.Y = SlideSign * 300;

        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在初始化主界面（步骤 1/2）");
        SettingsService.PropertyChanged += (sender, args) =>
        {
            LoadSettings();
        };
        LessonsService.PreMainTimerTicked += LessonsServiceOnPreMainTimerTicked;
        LessonsService.PostMainTimerTicked += LessonsServiceOnPostMainTimerTicked;
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        RulesetService.StatusUpdated += RulesetServiceOnStatusUpdated;
        TouchInFadingTimer.Tick += TouchInFadingTimerOnTick;
        IsRunningCompatibleMode = SettingsService.Settings.IsCompatibleWindowTransparentEnabled;
        TaskBarIconService.MoreOptionsMenu = MoreOptionsMenu;
        WindowRuleService.ForegroundWindowChanged += WindowRuleServiceOnForegroundWindowChanged;
        HighFreqTopmostRecheckTimer.Tick += HighFreqTopmostRecheckTimerOnTick;
        
        this.UseMyWindowExt();
        
        if (OperatingSystem.IsWindows() && Environment.OSVersion.Version <= WindowsVersions.Win10V1803)
        {
            PseudoClasses.Set(":no-windowed-transparent", true);
        }

    }

    private void PostInit()
    {
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在初始化托盘菜单");
        var menu = this.FindResource("AppMenu") as NativeMenu;
        TaskBarIconService.MainTaskBarIcon.Menu = menu;
        TaskBarIconService.MainTaskBarIcon.IsVisible = true;
        TaskBarIconService.MainTaskBarIcon.Clicked += MainTaskBarIconOnClicked;
        if (!OperatingSystem.IsMacOS())
        {
            PopupHelper.DisablePopupsRequested += (_, _) =>
            {
                TaskBarIconService.MainTaskBarIcon.Menu = null;
            };
            PopupHelper.RestorePopupsRequested += (_, _) =>
            {
                TaskBarIconService.MainTaskBarIcon.Menu = menu;
            };
        }
        ViewModel.OverlayRemainTimePercents = 0.5;
        DiagnosticService.EndStartup();

        if (!ViewModel.Settings.IsNotificationEffectRenderingScaleAutoSet)
        {
            AutoSetNotificationEffectRenderingScale();
        }

        UriNavigationService.HandleAppNavigation("class-swap", args => OpenClassSwapWindow());

        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在初始化输入");
        if (SettingsService.Settings.UseRawInput)
        {
            try
            {
                InitializeRawInputHandler();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "无法初始化 RawInput，已回退到兼容模式。");
                LessonsService.PreMainTimerTicked += ProcessMousePos;
                ViewModel.Settings.IsErrorLoadingRawInput = true;
            }
        }
        else
        {
            LessonsService.PreMainTimerTicked += ProcessMousePos;
        }

        ComponentPresenter.SetIsMainWindowLoaded(this, true);
        RefreshDefaultMainWindowFont();
        StartupCompleted?.Invoke(this, EventArgs.Empty);

        if (!string.IsNullOrWhiteSpace(App.ApplicationCommand.Uri))
        {
            try
            {
                UriNavigationService.NavigateWrapped(new Uri(App.ApplicationCommand.Uri));
            }   
            catch (Exception ex)
            {
                // ignored
            }
        }
#if DEBUG
        MemoryProfiler.GetSnapshot("MainWindow OnContentRendered");
#endif
    }

    public override void Show()
    {
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在加载界面主题（2）");
        UpdateTheme();
        base.Show();
        UpdateWindowPos();
        Win32Properties.AddWndProcHookCallback(this, ProcWnd);
        Dispatcher.UIThread.InvokeAsync(PostInit, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var span = SentrySdk.GetSpan()?.StartChild("startup-initialize-mainWindow");
        ViewModel.Profile.PropertyChanged += (sender, args) => SaveProfile();
        ViewModel.Settings.PropertyChanged += SettingsOnPropertyChanged;
        LoadSettings();
        //ViewModel.CurrentProfilePath = ViewModel.Settings.SelectedProfile;
        LoadProfile();
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在加载界面主题（1）");
        // UpdateTheme();
        UserPrefrenceUpdateStopwatch.Start();
        AppBase.Current.PlatformSettings!.ColorValuesChanged += OnSystemEventsOnUserPreferenceChanged;
        AppBase.Current.AppStopping += (sender, args) => AppBase.Current.PlatformSettings!.ColorValuesChanged -= OnSystemEventsOnUserPreferenceChanged;
        span?.Finish();
    }
    
    private void InitializeRawInputHandler()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("RawInput仅在Windows平台受支持。");
        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse,
            RawInputDeviceFlags.InputSink, handle);
        RawInputDevice.RegisterDevice(HidUsageAndPage.TouchScreen,
            RawInputDeviceFlags.InputSink, handle);

        RawInputUpdateStopWatch.Start();
    }

    private void AutoSetNotificationEffectRenderingScale()
    {
        var screen = GetSelectedScreenSafe();
        if (screen == null)
            return;
        if (screen.Bounds.Height >= 1400)
        {
            ViewModel.Settings.NotificationEffectRenderingScale = 0.75;
        }
        if (screen.Bounds.Height >= 2000)
        {
            ViewModel.Settings.NotificationEffectRenderingScale = 0.5;
        }

        ViewModel.Settings.IsNotificationEffectRenderingScaleAutoSet = true;
    }

    private void LoadSettings()
    {
        var r = SettingsService.Settings;
        ViewModel.Settings = r;
        ViewModel.Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public void LoadProfile()
    {
        //ProfileService.LoadProfile();
        ViewModel.Profile = ProfileService.Profile;
    }

    private void RefreshDefaultMainWindowFont()
    {
        if (ViewModel.Settings.MainWindowFont != DefaultFontFamilyKey)
        {
            return;
        }

        SettingsService.Settings.NotifyPropertyChanged("MainWindowFontWeight2");
    }
    #endregion

    #region Event Handlers
    
    private void HighFreqTopmostRecheckTimerOnTick(object? sender, EventArgs e)
    {
        if (ViewModel.Settings.WindowTopmostRecheckMode == 3)
        {
            ReCheckTopmostState();
            SetBottom();
        }
    }

    private void WindowRuleServiceOnForegroundWindowChanged(object? sender, ForegroundWindowChangedEventArgs e)
    {
        if (ViewModel.Settings.WindowTopmostRecheckMode == 1)
        {
            ReCheckTopmostState();
            SetBottom();
        }
    }

    private void TouchInFadingTimerOnTick(object? sender, EventArgs e)
    {
        ViewModel.IsMouseIn = false;
        TouchInFadingTimer.Stop();
    }

    private void RulesetServiceOnStatusUpdated(object? sender, EventArgs e)
    {
        if (ViewModel.Settings.HideMode == 1)
        {
            ViewModel.IsHideRuleSatisfied = RulesetService.IsRulesetSatisfied(ViewModel.Settings.HideRules);
        }
        // Detect fullscreen
        var screen = GetSelectedScreenSafe();
        if (screen != null)
        {
            ViewModel.IsForegroundFullscreen = PlatformServices.WindowPlatformService.IsForegroundWindowFullscreen(screen);
            ViewModel.IsForegroundMaxWindow = PlatformServices.WindowPlatformService.IsForegroundWindowMaximized(screen);
        }
    }

    private async void LessonsServiceOnPostMainTimerTicked(object? sender, EventArgs e)
    {
        if (ViewModel.Settings.WindowTopmostRecheckMode == 2)
        {
            ReCheckTopmostState();
            SetBottom();
        }
        UpdateBreakRotation();
    }

    private void LessonsServiceOnPreMainTimerTicked(object? sender, EventArgs e)
    {
        //SettingsService.Settings.IsNetworkConnect = InternetGetConnectedState(out var _);
        //SettingsService.Settings.IsNotificationSpeechEnabled = SettingsService.Settings.IsNetworkConnect || SettingsService.Settings.IsSystemSpeechSystemExist;
        if (SettingsService.Settings.IsMainWindowDebugEnabled)
            ViewModel.DebugCurrentTime = ExactTimeService.GetCurrentLocalDateTime();

        UpdateWindowPos(true);
        if (ViewModel.Settings.WindowLayer == 0)
        {
            //SetBottom();
        }
        //NotificationHostService.OnUpdateTimerTick(this, EventArgs.Empty);

        if (SettingsService.Settings.DebugTimeSpeed != 0)
        {
            SettingsService.Settings.DebugTimeOffsetSeconds += (SettingsService.Settings.DebugTimeSpeed - 1) * 0.05;
        }
    }

    public void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!CheckAccess())
        {
            return;
        }
        UpdateTheme();
        UpdateStyleStates();
        if (e.PropertyName is nameof(ViewModel.Settings.IsMouseInFadingReversed)
                           or nameof(ViewModel.Settings.IsMouseInFadingEnabled))
        {
            UpdateFadeStatus();
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsMouseIn))
        {
            UpdateFadeStatus();
        }

        if (e.PropertyName == nameof(ViewModel.IsProfileTabOpen))
        {
            UpdateProfileTabWindowState();
        }
        
    }

    private void OnSystemEventsOnUserPreferenceChanged(object? sender, PlatformColorValues args)
    {
        if (UserPrefrenceUpdateStopwatch.ElapsedMilliseconds < 1000)
        {
            return;
        }
        //Debug.WriteLine("Updated theme.");
        UserPrefrenceUpdateStopwatch.Restart();
        UpdateTheme();
    }

    private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!ViewModel.IsClosing && (e.CloseReason != WindowCloseReason.OSShutdown &&
                                     e.CloseReason != WindowCloseReason.ApplicationShutdown))
        {
            e.Cancel = true;
            return;
        }
        AppBase.Current.Stop();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        BeginWatchEntranceAnimation();
    }

    #region 主界面入场/收起动画（双色块依次滑入，主界面文字随色块同步滑入；无时间/课程时反向播放收起）

    private bool _entranceAnimationPlayed;
    private bool _entranceAnimationWatching;
    private bool _welcomeShown;
    private bool _presenceBusy;
    private bool? _pendingPresent;

    /// <summary>主界面逻辑状态：true=显示（有时间/课程），false=已收起（无时间/课程）。</summary>
    private bool _isInterfacePresent = true;

    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan EntranceDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>窗口停靠在正下（WindowDockingLocation=4）时动画从下向上，其余（正上）从上向下。</summary>
    private bool IsBottomDocking => ViewModel.Settings.WindowDockingLocation == 4;

    private double SlideSign => IsBottomDocking ? 1.0 : -1.0;

    /// <summary>当前是否需要显示主界面：课间/准备上课时显示；开始上课时收起（播放收起动画），除非设置了上课不隐藏。</summary>
    private bool HasTimeOrCourse =>
        LessonsService.CurrentState is TimeState.Breaking or TimeState.PrepareOnClass
        || (LessonsService.CurrentState == TimeState.OnClass && !SettingsService.Settings.HideOnClass);

    /// <summary>主界面内容滑入/滑出用的位移变换（位于 ZoomBorder 上）。</summary>
    private TranslateTransform? _contentSlideTransform;

    private TranslateTransform ContentSlideTransform =>
        _contentSlideTransform ??= (ZoomBorder.RenderTransform as TranslateTransform)!;

    /// <summary>
    /// 等待主界面真正可见、布局就绪后播放入场动画（整个生命周期只播一次）。
    /// </summary>
    private void BeginWatchEntranceAnimation()
    {
        if (_entranceAnimationWatching)
        {
            return;
        }
        _entranceAnimationWatching = true;
        // 防闪现：启动时先把主界面内容移出屏幕，等动画开始后再滑入。
        ResetContentOffscreen();
        LessonsService.CurrentTimeStateChanged += LessonsServiceOnCurrentTimeStateChanged;
        GridRoot.PropertyChanged += GridRootVisibleChangedForEntrance;
        SizeChanged += MainWindowSizeChangedForEntrance;
        TryPlayEntranceAnimation();
    }

    private void ResetContentOffscreen()
    {
        var height = Math.Max(Bounds.Height, EntranceCurtainHost.Bounds.Height);
        if (height < 10)
        {
            height = 300; // 布局未完成时先给一个足够大的偏移，避免内容闪现
        }
        ContentSlideTransform.Y = SlideSign * height;
    }

    private void GridRootVisibleChangedForEntrance(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(Visual.IsVisible))
        {
            TryPlayEntranceAnimation();
        }
    }

    private void MainWindowSizeChangedForEntrance(object? sender, SizeChangedEventArgs e)
    {
        TryPlayEntranceAnimation();
    }

    private async void TryPlayEntranceAnimation()
    {
        if (_entranceAnimationPlayed || !GridRoot.IsVisible || Bounds.Height < 10)
        {
            return;
        }
        _entranceAnimationPlayed = true;
        GridRoot.PropertyChanged -= GridRootVisibleChangedForEntrance;
        SizeChanged -= MainWindowSizeChangedForEntrance;
        try
        {
            await PlayEntranceAsync();
            _isInterfacePresent = true;
            if (!_welcomeShown)
            {
                _welcomeShown = true;
                await ShowWelcomeAsync();
            }
            // 启动流程结束后，按当前时间状态决定是否收起（无时间/课程时反向播放动画收起）。
            await ApplyPresenceAsync();
        }
        catch (Exception ex)
        {
            ContentSlideTransform.Y = 0;
            EntranceCurtainHost.IsVisible = false;
            WelcomeOverlay.IsVisible = false;
            _isInterfacePresent = true;
            ViewModel.IsMainWindowCollapsed = false;
            Logger.LogError(ex, "播放主界面入场动画失败");
        }
    }

    private void LessonsServiceOnCurrentTimeStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () => await ApplyPresenceAsync());
    }

    /// <summary>按当前时间状态应用主界面显示/收起（上课开始与无时间/课程时反向播放入场动画收起）。</summary>
    private async Task ApplyPresenceAsync()
    {
        // 首次入场动画完成前不做自动收起/显示，避免和启动流程冲突。
        // 快进预览期间不抑制：让上课收起/课间入场动画随状态切换正常播放，便于检查。
        if (!_entranceAnimationPlayed)
        {
            return;
        }
        await SetMainInterfacePresentAsync(HasTimeOrCourse);
    }

    /// <summary>显示或收起主界面（带动画），忙时记录最新目标，空闲后继续处理。</summary>
    private async Task SetMainInterfacePresentAsync(bool present)
    {
        if (_presenceBusy)
        {
            _pendingPresent = present;
            return;
        }
        _presenceBusy = true;
        try
        {
            while (true)
            {
                var target = present;
                _pendingPresent = null;
                if (target && !_isInterfacePresent)
                {
                    ViewModel.IsMainWindowCollapsed = false;
                    await PlayEntranceAsync();
                    _isInterfacePresent = true;
                }
                else if (!target && _isInterfacePresent)
                {
                    await PlayCollapseAsync();
                    ViewModel.IsMainWindowCollapsed = true;
                    _isInterfacePresent = false;
                }
                if (_pendingPresent is null || _pendingPresent == target)
                {
                    break;
                }
                present = _pendingPresent.Value;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "播放主界面动画失败");
            ContentSlideTransform.Y = 0;
            EntranceCurtainHost.IsVisible = false;
        }
        finally
        {
            _presenceBusy = false;
        }
    }

    /// <summary>测试动画：重播入场动画（忽略当前状态）。</summary>
    public async Task PlayEntranceAnimationForTestAsync()
    {
        if (_presenceBusy)
        {
            return;
        }
        _presenceBusy = true;
        try
        {
            ViewModel.IsMainWindowCollapsed = false;
            _isInterfacePresent = true;
            await PlayEntranceAsync();
        }
        finally
        {
            _presenceBusy = false;
        }
    }

    /// <summary>测试动画：播放收起动画（反向入场动画）。</summary>
    public async Task PlayCollapseAnimationForTestAsync()
    {
        if (_presenceBusy)
        {
            return;
        }
        _presenceBusy = true;
        try
        {
            if (_isInterfacePresent && !ViewModel.IsMainWindowCollapsed)
            {
                await PlayCollapseAsync();
                ViewModel.IsMainWindowCollapsed = true;
                _isInterfacePresent = false;
            }
            else
            {
                // 已收起时先短暂显示再收起，便于观察完整动画
                ViewModel.IsMainWindowCollapsed = false;
                ContentSlideTransform.Y = 0;
                await Task.Delay(300);
                await PlayCollapseAsync();
                ViewModel.IsMainWindowCollapsed = true;
                _isInterfacePresent = false;
            }
        }
        finally
        {
            _presenceBusy = false;
        }
    }

    /// <summary>测试动画：显示 5 秒启动欢迎信息。</summary>
    public async Task ShowWelcomeForTestAsync()
    {
        await ShowWelcomeAsync();
    }

    /// <summary>显示 5 秒启动欢迎信息。</summary>
    private async Task ShowWelcomeAsync()
    {
        WelcomeOverlay.IsVisible = true;
        try
        {
            await Task.Delay(5000);
        }
        finally
        {
            WelcomeOverlay.IsVisible = false;
        }
    }

    private async Task PlayEntranceAsync()
    {
        var height = Math.Max(EntranceCurtainHost.Bounds.Height, Bounds.Height);
        if (height <= 0)
        {
            ContentSlideTransform.Y = 0;
            return;
        }
        var sign = SlideSign;

        // 后幕=白色块先进入，前幕=可编辑的主界面颜色块随后进入并盖住它。
        EntranceCurtainBack.Background = Brushes.White;
        EntranceCurtainFront.Background = new SolidColorBrush(ViewModel.Settings.MainWindowCurtainColor);

        // 关键：动画开始前把幕布和主界面内容都移到画面外，避免显示瞬间闪现。
        SetCurtainOffset(EntranceCurtainBack, sign * height);
        SetCurtainOffset(EntranceCurtainFront, sign * height);
        ContentSlideTransform.Y = sign * height;
        EntranceCurtainHost.IsVisible = true;

        // 等窗口真正呈现，避免动画在窗口可见前就播完。
        await Task.Delay(250);

        // 白块先入；主界面内容与主色块同步滑入（文字随蓝条一起滑进）。
        var backTask = RunCurtainSlideInAsync(EntranceCurtainBack, TimeSpan.Zero, sign * height);
        var frontTask = RunCurtainSlideInAsync(EntranceCurtainFront, EntranceDelay, sign * height);
        var contentTask = RunContentSlideInAsync(EntranceDelay, sign * height);
        await Task.WhenAll(backTask, frontTask, contentTask);

        // 揭幕：幕布隐去，露出主界面。前幕与主界面横条同色，揭幕不会出现颜色跳变。
        EntranceCurtainHost.IsVisible = false;
    }

    private async Task PlayCollapseAsync()
    {
        var height = Math.Max(EntranceCurtainHost.Bounds.Height, Bounds.Height);
        if (height <= 0)
        {
            ContentSlideTransform.Y = SlideSign * 300;
            return;
        }
        var sign = SlideSign;

        // 收起：先让主色幕布与主界面同色铺满（视觉无缝），再整体反向滑出。
        EntranceCurtainBack.Background = Brushes.White;
        EntranceCurtainFront.Background = new SolidColorBrush(ViewModel.Settings.MainWindowCurtainColor);
        SetCurtainOffset(EntranceCurtainBack, 0);
        SetCurtainOffset(EntranceCurtainFront, 0);
        ContentSlideTransform.Y = 0;
        EntranceCurtainBack.IsVisible = true;
        EntranceCurtainFront.IsVisible = true;
        EntranceCurtainHost.IsVisible = true;

        await Task.Delay(250);

        // 反着播进入动画：主色块与内容先滑出，白块延迟随后滑出。
        var frontTask = RunCurtainSlideOutAsync(EntranceCurtainFront, TimeSpan.Zero, sign * height);
        var contentTask = RunContentSlideOutAsync(TimeSpan.Zero, sign * height);
        var backTask = RunCurtainSlideOutAsync(EntranceCurtainBack, EntranceDelay, sign * height);
        await Task.WhenAll(frontTask, contentTask, backTask);

        EntranceCurtainHost.IsVisible = false;
        ContentSlideTransform.Y = sign * height; // 保持屏幕外，等待下次显示
    }

    private static void SetCurtainOffset(Border curtain, double y)
    {
        if (curtain.RenderTransform is TranslateTransform transform)
        {
            transform.Y = y;
        }
    }

    /// <summary>幕布从屏幕外（startOffset）滑入到 0。</summary>
    private async Task RunCurtainSlideInAsync(Border curtain, TimeSpan begin, double startOffset)
    {
        if (curtain.RenderTransform is not TranslateTransform transform)
        {
            return;
        }
        transform.Y = startOffset;
        if (begin > TimeSpan.Zero)
        {
            await Task.Delay(begin);
        }
        await EaseTransformAsync(transform, startOffset, 0);
    }

    /// <summary>幕布从 0 滑出到屏幕外（endOffset）。</summary>
    private async Task RunCurtainSlideOutAsync(Border curtain, TimeSpan begin, double endOffset)
    {
        if (curtain.RenderTransform is not TranslateTransform transform)
        {
            return;
        }
        transform.Y = 0;
        if (begin > TimeSpan.Zero)
        {
            await Task.Delay(begin);
        }
        await EaseTransformAsync(transform, 0, endOffset);
    }

    /// <summary>主界面内容从屏幕外（startOffset）滑入到 0，与主色块同步。</summary>
    private async Task RunContentSlideInAsync(TimeSpan begin, double startOffset)
    {
        ContentSlideTransform.Y = startOffset;
        if (begin > TimeSpan.Zero)
        {
            await Task.Delay(begin);
        }
        await EaseTransformAsync(ContentSlideTransform, startOffset, 0);
    }

    /// <summary>主界面内容从 0 滑出到屏幕外（endOffset），与主色块同步。</summary>
    private async Task RunContentSlideOutAsync(TimeSpan begin, double endOffset)
    {
        ContentSlideTransform.Y = 0;
        if (begin > TimeSpan.Zero)
        {
            await Task.Delay(begin);
        }
        await EaseTransformAsync(ContentSlideTransform, 0, endOffset);
    }

    private static async Task EaseTransformAsync(TranslateTransform transform, double from, double to)
    {
        var stopwatch = Stopwatch.StartNew();
        var total = EntranceDuration.TotalMilliseconds;
        while (stopwatch.ElapsedMilliseconds < total)
        {
            var t = Math.Min(stopwatch.ElapsedMilliseconds / total, 1.0);
            // easeInOutCubic：非线性缓入缓出。
            var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            transform.Y = from + (to - from) * eased;
            await Task.Delay(16);
        }
        transform.Y = to;
    }

    #endregion

    private void LayoutContainerGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Dispatcher.UIThread.InvokeAsync(() => Height = LayoutContainerGrid.Bounds.Height, DispatcherPriority.Render);
    }
    #endregion

    #region Input Handling
    private bool _isPointerOverMainWindow;

    private void UpdateMouseStatus()
    {
        try
        {
            var ptr = PlatformServices.WindowPlatformService.GetMousePos();
            _isPointerOverMainWindow = IsPointerOverWindowRect(ptr);
            MousePosChanged?.Invoke(this, new MousePosChangedEventArgs(ptr));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "无法更新鼠标状态。");
        }
    }

    /// <summary>
    /// 判断指针是否落在主界面窗口范围内（物理像素坐标）。
    /// </summary>
    private bool IsPointerOverWindowRect(Point ptr)
    {
        try
        {
            GetCurrentDpi(out var dpiX, out var dpiY);
            var origin = Avalonia.VisualExtensions.PointToScreen(this, new Point(0, 0));
            return ptr.X >= origin.X && ptr.Y >= origin.Y &&
                   ptr.X <= origin.X + Bounds.Width * dpiX &&
                   ptr.Y <= origin.Y + Bounds.Height * dpiY;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "无法获取主界面窗口范围");
            return false;
        }
    }

    private void MainTaskBarIconOnClicked(object? sender, EventArgs e)
    {
        switch (SettingsService.Settings.TaskBarIconClickBehavior)
        {
            case 0:
                if (!OperatingSystem.IsWindows()) // 兼容性处理：“打开主菜单”左键托盘行为仅在Windows平台上有效，为应对档案从Windows平台迁移至其他平台的情况，此处将左键托盘行为更改为“打开设置窗口”
                {
                    SettingsService.Settings.TaskBarIconClickBehavior = 4;
                    break;
                }
                // Get this tray icon's implementation
                ITrayIconImpl? impl = (ITrayIconImpl?)typeof(TrayIcon)
                    .GetProperty ("Impl", BindingFlags.NonPublic | BindingFlags.Instance)?
                    .GetValue (TaskBarIconService.MainTaskBarIcon);

                // Get the Windows tray icon implementation type
                Type? type = AppDomain.CurrentDomain.GetAssemblies ()
                    .Where (a => a.FullName?.StartsWith ("Avalonia.Win32") ?? false)
                    .SelectMany (a => a.GetTypes ())
                    .FirstOrDefault (t => t.Name == "TrayIconImpl");

                // If the Implementation and type are not null
                if (impl != null && type != null)
                {
                    // Get the OnRightClicked method
                    MethodInfo? methodInfo = type.GetMethod("OnRightClicked",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    // Invoke the method on the implementation
                    methodInfo?.Invoke(impl, null);
                }

                break;
            case 1:
                OpenProfileSettingsWindow();
                break;
            case 2:
                SettingsService.Settings.IsMainWindowVisible = !SettingsService.Settings.IsMainWindowVisible;
                break;
            case 3:
                OpenClassSwapWindow();
                break;
            case 4:
                App.GetService<SettingsWindowNew>().Open();
                break;
        }
    }

    private void ProcessMousePos(object? sender, EventArgs e)
    {
        UpdateMouseStatus();
    }

    private IntPtr ProcWnd(IntPtr hwnd, uint msg, IntPtr param, IntPtr lParam, ref bool handled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return nint.Zero;
        } 
        if (msg == 0x00FF) // WM_INPUT
        {
            if (RawInputUpdateStopWatch.ElapsedMilliseconds < 20)
            {
                return IntPtr.Zero;
            }
            RawInputUpdateStopWatch.Restart();
            // Create an RawInputData from the handle stored in lParam.
            var data = RawInputData.FromHandle(lParam);
            RawInputEvent?.Invoke(this, new RawInputEventArgs(data));
        }
        
        if (msg == 0x0047) // WM_WINDOWPOSCHANGED
        {
            var pos = Marshal.PtrToStructure<NativeWindowHelper.WINDOWPOS>(lParam);
            Logger.LogTrace("WM_WINDOWPOSCHANGED {}", pos.flags);
            if ((pos.flags & NativeWindowHelper.SWP_NOZORDER) == 0 && ViewModel.Settings.WindowTopmostRecheckMode == 0) // SWP_NOZORDER
            {
                Logger.LogTrace("Z order changed");
                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_TOPMOST)
                {
                    ReCheckTopmostState();
                }
        
                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_BOTTOM)
                {
                    SetBottom();
                }
            }
        }

        return nint.Zero;
    }
    #endregion

    #region Theme & Settings
    
    public void SaveProfile()
    {
        ProfileService.SaveProfile(ViewModel.CurrentProfilePath);
    }
    
    private void UpdateFadeStatus()
    {
        ViewModel.IsMainWindowFaded =
            ViewModel.Settings.IsMouseInFadingEnabled &&
           (ViewModel.IsMouseIn ^ ViewModel.Settings.IsMouseInFadingReversed);
    }

    private void UpdateStyleStates()
    {
        PseudoClasses.Set(":dock-top", ViewModel.Settings.WindowDockingLocation is 0 or 1 or 2);
        PseudoClasses.Set(":dock-bottom", ViewModel.Settings.WindowDockingLocation is 3 or 4 or 5);
    }

    private async void UpdateTheme()
    {
        HighFreqTopmostRecheckTimer.IsEnabled = ViewModel.Settings.WindowTopmostRecheckMode == 3;
        if (ViewModel.Settings.IsMouseClickingEnabled)
        {
            ViewModel.Settings.IsMouseClickingEnabled = false;
            await PlatformServices.DesktopToastService.ShowToastAsync("已禁用不支持的设置", "【启用鼠标点击】设置项目不再受到支持并已自动禁用，感谢您的支持与理解。");
        }
        
        UpdateWindowPos();
        UpdateWindowFeatures();
        UpdateWindowLayer();

        if (ResourceLoaderBorder != null)
        {
            ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowSecondaryFontSize)] =
                SettingsService.Settings.MainWindowSecondaryFontSize;
            ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowBodyFontSize)] =
                SettingsService.Settings.MainWindowBodyFontSize;
            ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowEmphasizedFontSize)] =
                SettingsService.Settings.MainWindowEmphasizedFontSize;
            ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowLargeFontSize)] =
                SettingsService.Settings.MainWindowLargeFontSize;
            ControlColorHelper.SetControlForegroundColor(ResourceLoaderBorder, ViewModel.Settings.MainWindowFontColor,
                true);
        }

        App._isCriticalSafeModeEnabled = ViewModel.Settings.IsCriticalSafeMode;
        SizeToContent = SizeToContent.Height;
    }
    #endregion

    #region Windowing
    private void StackPanelRootContainer_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateWindowPos();
    }
    
    private void ReCheckTopmostState()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (ViewModel.IsNotificationWindowExplicitShowed || ViewModel.Settings.WindowLayer == 1)
        {
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Topmost, true);
            //Topmost = true;
        }
    }

    
    internal Screen? GetSelectedScreenSafe()
    {
        return ViewModel.Settings.WindowDockingMonitorIndex < Screens.ScreenCount 
               && ViewModel.Settings.WindowDockingMonitorIndex >= 0
            ? Screens.All[ViewModel.Settings.WindowDockingMonitorIndex]
            : Screens.Primary;
    }
    
    private void SetBottom()
    {
        if (ViewModel.Settings.WindowLayer != 0)
        {
            return;
        }
        if (ViewModel.IsNotificationWindowExplicitShowed)
        {
            //SetWindowPos(hWnd, NativeWindowHelper.HWND_TOPMOST, 0, 0, 0, 0,
            //    SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            return;
        }
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Bottommost, true);
    }

    private void UpdateWindowFeatures()
    {
        var isCaptureBlocked = ViewModel.Settings.IsWindowCaptureBlockingEnabled;
        var shouldUseToolWindow = ViewModel is
        {
            Settings.IsScreenRecordingModeEnabled: false
        };
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.ToolWindow, shouldUseToolWindow);
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Transparent, true);
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Private, isCaptureBlocked);
    }

    private void UpdateWindowLayer()
    {
        switch (ViewModel.Settings.WindowLayer)
        {
            case 0: // bottom
                Topmost = ViewModel.IsNotificationWindowExplicitShowed;
                break;
            case 1:
                Topmost = true;
                break;
        }

        var shouldSkipManagement = Topmost;
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.SkipManagement, shouldSkipManagement);
        if (Topmost)
        {
            // 防止窗口在切换层级时残留 Bottommost 状态，导致“已激活但仍在最底层”。
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Bottommost, false);
        }
        PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Topmost, Topmost);
    }

    private void OldWindowPosUpdateImpl(bool updateEffectWindow)
    {
        GetCurrentDpi(out var dpiX, out var dpiY);

        var scale = ViewModel.Settings.Scale;
        ViewModel.GridRootLeft = Width / 10 * (scale - 1);
        ViewModel.GridRootTop = Height / 10 * (scale - 1);
        
        var screen = GetSelectedScreenSafe();
        if (screen == null)
            return;
        double offsetAreaTop = screen.Bounds.Y;
        double offsetAreaBottom = screen.Bounds.Bottom;
        Width = screen.Bounds.Width / dpiX;
        var aw = Bounds.Width * dpiX;
        var ah = Bounds.Height * dpiY;
        var ox = ViewModel.Settings.WindowDockingOffsetX;
        var oy = ViewModel.Settings.WindowDockingOffsetY;
        //Height = GridRoot.ActualHeight * scale;
        
        var dockingTop = ViewModel.Settings.WindowDockingLocation is 0 or 1 or 2;
        var verticalSafeAreaPx = 0.0;
        var safeT = Math.Max(dockingTop ? Math.Min(verticalSafeAreaPx, oy) : verticalSafeAreaPx, 0) * scale;
        var safeB = Math.Max(dockingTop ? verticalSafeAreaPx : Math.Min(verticalSafeAreaPx, -oy), 0) * scale;
        var x = screen.Bounds.X;
        // 和 WPF 不同，Avalonia 定位窗口用的基于物理屏幕的像素坐标，而非逻辑坐标，无需 dpi 转换。
        var y = dockingTop 
            ? offsetAreaTop + oy - safeT
            : offsetAreaBottom - ah + oy + safeB;
        var clientBoundsRelative = new PixelRect(0, (int)(safeT * dpiY), (int)aw, (int)ah)
            .ToRectWithDpi(new Vector(dpiX * 96, dpiY * 96));
        ViewModel.ActualClientBound = clientBoundsRelative;
        if (LayoutContainerGrid != null)
        {
            LayoutContainerGrid.Width = Width = screen.Bounds.Width / dpiX;
            LayoutContainerGrid.Height = Height = RootLayoutTransformControl.Bounds.Height + safeT + safeB;
        }
        ViewModel.ActualRootOffsetX = 0;
        ViewModel.ActualRootOffsetY = 0;
        var newPos = new PixelPoint((int)x, (int)y);
        if (Position != newPos)
        {
            Position = newPos;
        }
        WindowState = WindowState.Normal;
        
        if (updateEffectWindow)
        {
            TopmostEffectWindow.UpdateWindowPos(screen, 1 / dpiX, ViewModel.IsForegroundFullscreen);
        }
    }

    private void NewWindowPosUpdateImpl()
    {
        GetCurrentDpi(out var dpiX, out var dpiY);

        var scale = ViewModel.Settings.Scale;
        ViewModel.GridRootLeft = Width / 10 * (scale - 1);
        ViewModel.GridRootTop = Height / 10 * (scale - 1);

        WindowState = WindowState.Normal;
        var screen = GetSelectedScreenSafe();
        if (screen == null)
            return;
        var clientRect = screen.Bounds;
        var ox = ViewModel.Settings.WindowDockingOffsetX / dpiX;
        var oy = ViewModel.Settings.WindowDockingOffsetY / dpiY;
        var fullscreen = ViewModel.IsForegroundFullscreen;
        var bounds = fullscreen ? screen.Bounds : clientRect;
        var relativeX = 0;
        var relativeY = clientRect.Y - screen.Bounds.Y;
        var width = bounds.Width;
        var height = bounds.Height;
        // 创建相对矩形
        var clientBoundsRelative = new PixelRect(relativeX, relativeY, width, height)
            .ToRectWithDpi(new Vector(dpiX * 96, dpiY * 96));
        ViewModel.ActualClientBound = clientBoundsRelative;
        if (LayoutContainerGrid != null) 
        {
            LayoutContainerGrid.Width = Width = screen.Bounds.Width / dpiX;
            LayoutContainerGrid.Height = Height = (screen.Bounds.Height - 1)  // 防止 Windows 发电误以为是全屏
                                                  / dpiY;
        }
        ViewModel.ActualRootOffsetX = ox;
        ViewModel.ActualRootOffsetY = oy;
        var newPos = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
        if (Position != newPos)
        {
            Position = newPos;
        }
    }

    private void UpdateWindowPos(bool updateEffectWindow=false)
    {
        OldWindowPosUpdateImpl(updateEffectWindow);
    }
    
    public void GetCurrentDpi(out double dpiX, out double dpiY, Visual? visual=null)
    {
        dpiX = _latestDpiX;
        dpiY = _latestDpiY;
        if (ViewModel.IsClosing)
        {
            return;
        }
        try
        {
            var screen = GetSelectedScreenSafe() ?? Screens.ScreenFromWindow(this);
            dpiX = screen?.Scaling ?? 1.0;
            dpiY = screen?.Scaling ?? 1.0;
        }
        catch(Exception ex)
        {
            Logger.LogError(ex, "无法获取当前dpi");
        }
    }

    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        //UpdateWindowPos();
    }

    private void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        SetBottom();
        // 直接监听窗口属性变化并设置鼠标穿透可能导致死循环/栈溢出，故我们在用户点到 ClassIsland 窗口时设置这个。
        UpdateWindowFeatures();
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        SetBottom();
    }
    
    public void AcquireTopmostLock(object o)
    {
        var prevEmpty = TopmostLocks.Count <= 0;
        if (TopmostLocks.Contains(o))
        {
            return;
        }
        TopmostLocks.Add(o);
        if (!prevEmpty)
        {
            return;
        }

        ViewModel.IsNotificationWindowExplicitShowed = true;
        if (ViewModel.IsNotificationWindowExplicitShowed && SettingsService.Settings.WindowLayer == 0)
        {
            UpdateWindowLayer();
            ReCheckTopmostState();
        }
    }

    public void ReleaseTopmostLock(object o)
    {
        TopmostLocks.RemoveAll(x => x == o);

        if (TopmostLocks.Count > 0)
        {
            return;
        }
        
        if (ViewModel.IsNotificationWindowExplicitShowed)
        {
            ViewModel.IsNotificationWindowExplicitShowed = false;
            SetBottom();
            UpdateWindowLayer();
        }
    }
    #endregion

    #region Menu Items
    

    private void ButtonSettings_OnClick(object sender, EventArgs e)
    {
        OpenProfileSettingsWindow();
    }

    private void ButtonResizeDebug_OnClick(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private void MenuItemSettings_OnClick(object sender, EventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open();
    }


    private async void MenuItemExitApp_OnClick(object sender, EventArgs e)
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }
        
        ViewModel.IsClosing = true;
        Close();
        AppBase.Current.Stop();
    }
    private void MenuItemRestartApp_OnClick(object sender, EventArgs e)
    {
        AppBase.Current.Restart();
    }
    
    private void MenuItemTemporaryClassPlan_OnClick(object sender, EventArgs e)
    {
        App.GetService<ProfileSettingsWindow>().OpenDrawer("TemporaryClassPlan");
        OpenProfileSettingsWindow();
    }
    
    private void MenuItemAbout_OnClick(object sender, EventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open("about");
    }

    private void MenuItemHelps_OnClick(object sender, EventArgs e)
    {
        UriNavigationService.Navigate(new Uri("https://docs.classisland.tech/app/"));
    }

    private void MenuItemUpdates_OnClick(object sender, EventArgs e)
    {
        // App.GetService<SettingsWindowNew>().Open("update");
    }

    private void GridRoot_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
    }

    private async void MenuItemDebugFitSize_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OverlayRemainTimePercents = 0.5;
    }

    private void MenuItemClearAllNotifications_OnClick(object sender, EventArgs e)
    {
        NotificationHostService.CancelAllNotifications();
    }

    private void MenuItemNotificationSettings_OnClick(object sender, RoutedEventArgs e)
    {
        // App.GetService<SettingsWindowNew>().Open("notification");
    }

    private void MenuItemSwitchMainWindowVisibility_OnClick(object sender, EventArgs e)
    {
        ViewModel.Settings.IsMainWindowVisible = !ViewModel.Settings.IsMainWindowVisible;
    }

    private void MenuItemClassSwap_OnClick(object sender, EventArgs e)
    {
        OpenClassSwapWindow();
    }
    
    

    private void NativeMenuItemDebugDevTools_OnClick(object? sender, EventArgs e)
    {
        RaiseEvent(new KeyEventArgs()
        {
            Key = Key.F12,
            RoutedEvent = KeyDownEvent
        });
    }

    private async void NativeMenuItemDebugEnableTempClassPlan_OnClick(object? sender, EventArgs e)
    {
        var input = new TextBox();
        var dialog = new TaskDialog()
        {
            Header = "启用临时课表",
            Content = new StackPanel()
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock()
                    {
                        Text = $"输入课表 GUID，当前档案为 {ProfileService.CurrentProfilePath}"
                    },
                    input
                }
            },
            XamlRoot = this,
            Buttons =
            {
                TaskDialogButton.OKButton
            }
        };
        await dialog.ShowAsync();

        ProfileService.Profile.TempClassPlanId = Guid.TryParse(input.Text, out var guid) ? guid : Guid.Empty;
        ProfileService.Profile.TempClassPlanSetupTime = ExactTimeService.GetCurrentLocalDateTime();
    }

    private void NativeMenuItemDebugCrashTest_OnClick(object? sender, EventArgs e)
    {
        var window = new CrashWindow();
        window.Show();
    }

    private void NativeMenuItemDebugDevPortal_OnClick(object? sender, EventArgs e)
    {
        IAppHost.GetService<DevPortalWindow>().Show();
    }
    
    private void NativeMenuItemDebugOpenScreenshotWindow_OnClick(object? sender, EventArgs e)
    {
        IAppHost.GetService<ScreenshotHelperWindow>().Show();
    }
    
    #endregion

    #region Gateways

    private void ButtonOpenProfileTab_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsProfileTabOpen = true;
    }

    private void ButtonOpenMainTab_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsProfileTabOpen = false;
    }

    private void UpdateProfileTabWindowState()
    {
        if (ViewModel.IsProfileTabOpen)
        {
            var screen = GetSelectedScreenSafe();
            GetCurrentDpi(out var dpiX, out var dpiY);
            SizeToContent = SizeToContent.Manual;
            if (screen != null)
            {
                Width = screen.Bounds.Width / dpiX;
                Height = screen.Bounds.Height / dpiY;
                Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
            }
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Bottommost, false);
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Transparent, false);
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.ToolWindow, false);
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Topmost, true);
            Activate();
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            PlatformServices.WindowPlatformService.SetWindowFeature(this, WindowFeatures.Topmost, false);
            UpdateWindowLayer();
            UpdateWindowPos();
        }
    }

    public void OpenProfileSettingsWindow(Uri? uri = null)
    {
        if (ProfileTabContentHost.Content == null)
        {
            ProfileTabContentHost.Content = App.GetService<ProfileSettingsWindow>();
        }
        App.GetService<ProfileSettingsWindow>().Open(uri);
        ViewModel.IsProfileTabOpen = true;
    }

    private async void OpenClassSwapWindow()
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ChangeLessonsAuthorizeLevel))
        {
            return;
        }
        if (LessonsService.CurrentClassPlan == null) // 如果今天没有课程，则选择临时课表
        {
            App.GetService<ProfileSettingsWindow>().OpenDrawer("TemporaryClassPlan");
            OpenProfileSettingsWindow();
            return;
        }

        if (ClassChangingWindow != null)
        {
            return;
        }
        
        // ViewModel.IsBusy = true;
        ClassChangingWindow = new ClassChangingWindow()
        {
            ClassPlan = LessonsService.CurrentClassPlan
        };
        await ClassChangingWindow.ShowDialog(this);
        ClassChangingWindow.DataContext = null;
        ClassChangingWindow = null;
        // ViewModel.IsBusy = false;
    }
    
    #endregion

    #region Effects
    public void PlayEffect(INotificationEffectControl effect)
    {
        Logger.LogInformation("播放顶层特效：{}", effect);
        if (effect is not Control element)
            return;
        ViewModel.EffectControls.Add(element);
        if (!element.IsLoaded)
        {
            element.Loaded += (sender, args) => SetupEffectVisual(element, effect);
        }
        else
        {
            SetupEffectVisual(element, effect);
        }
    }

    private void SetupEffectVisual(Control visual1, INotificationEffectControl effect)
    {
        effect.EffectCompleted += (sender, args) =>
        {
            Logger.LogInformation("结束播放并移除顶层特效：{}", effect);
            ViewModel.EffectControls.Remove(visual1);
        };
        effect.Play();
    }
    #endregion

    #region Edit Mode

    private bool HasComponentWithSettings()
    {
        return ComponentsService.CurrentComponents.Lines
            .SelectMany(x => x.Children)
            .Any(x => x.AssociatedComponentInfo.SettingsType != null);
    }
    
    private bool HasContainerComponent()
    {
        return ComponentsService.CurrentComponents.Lines
            .SelectMany(x => x.Children)
            .Any(x => x.AssociatedComponentInfo.IsComponentContainer);
    }

    private void ListBoxMainWindowLineSettings_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not EditableComponentsListBox { Tag: MainWindowLineSettings settings } listBox)
        {
            return;
        }

        if (!ViewModel.ComponentsListBoxCache.Contains(listBox))
        {
            ViewModel.ComponentsListBoxCache.Add(listBox);
            if (ViewModel.SelectedComponentSettings != null && settings.Children.Contains(ViewModel.SelectedComponentSettings))
            {
                listBox.SelectedItem = ViewModel.SelectedComponentSettings;
            }
        }
        ViewModel.MainWindowLineListBoxCache[settings] = listBox;
        ViewModel.MainWindowLineListBoxCacheReversed[listBox] = settings;
    }

    private void ListBoxMainWindowLineSettings_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not EditableComponentsListBox listBox)
        {
            return;
        }

        var settings = ViewModel.MainWindowLineListBoxCacheReversed.GetValueOrDefault(listBox);
        if (settings != null)
        {
            ViewModel.MainWindowLineListBoxCache.Remove(settings);
        }
        ViewModel.MainWindowLineListBoxCacheReversed.Remove(listBox);
        while (ViewModel.ComponentsListBoxCache.Contains(listBox))
        {
            ViewModel.ComponentsListBoxCache.Remove(listBox);
        }
    }
    
    private void SelectorComponents_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count <= 0 || e.AddedItems[0] is not ComponentSettings settings 
            || sender is not EditableComponentsListBox { ItemsSource: IList<ComponentSettings> list } 
            || !list.Contains(settings))
        {
            // UpdateSettingsVisibility();
            return;
        }
        foreach (var listBox in ViewModel.ComponentsListBoxCache.Where(x => !Equals(x, sender)))
        {
            listBox.SelectedItem = null;
        }
        ViewModel.SelectedComponentSettings = settings;
    }

    private void ClearSelectedComponent()
    {
        foreach (var listBox in ViewModel.ComponentsListBoxCache)
        {
            listBox.SelectedItem = null;
        }

        ViewModel.SelectedComponentSettings = null;
    }

    private void EditableComponentsListBox_OnComponentDeleted(object? sender, EditableComponentsListBoxEventArgs e)
    {
        ClearSelectedComponent();
    }

    private void ToggleButtonIsMainLine_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button)
        {
            return;
        }

        foreach (var line in ComponentsService.CurrentComponents.Lines.Where(x => !Equals(button.DataContext, x)))
        {
            line.IsMainLine = false;
        }

        if (button.IsChecked == false)
        {
            var firstLine = ComponentsService.CurrentComponents.Lines.FirstOrDefault();
            if (firstLine != null) 
                firstLine.IsMainLine = true;
            this.ShowToast("已将第一行设置为主要行。");
        }
    }

    private void ToggleButtonIsNotificationEnabled_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (!ComponentsService.CurrentComponents.Lines.Any(x => x.IsNotificationEnabled))
        {
            this.ShowWarningToast("您已经禁用了所有主界面行的提醒显示功能。如果没有插件注册其它提醒消费者，提醒将不会显示，也不会播放提醒音效、特效和语音。");
        }
    }

    [RelayCommand]
    private void RemoveMainWindowLine(MainWindowLineSettings? settings)
    {
        if (settings == null)
        {
            return;
        }
        if (ComponentsService.CurrentComponents.Lines.Count <= 1)
        {
            this.ShowWarningToast("至少需要保留 1 个主界面行。");
            return;
        }

        ComponentsService.CurrentComponents.Lines.Remove(settings);
    }

    #endregion

    #region 快速预览今日时间表与课间内容轮换

    /// <summary>预览时每个时间点（上课/课间）播放的时长。</summary>
    private static readonly TimeSpan TodaySchedulePreviewItemDuration = TimeSpan.FromSeconds(10);

    private bool _isTodaySchedulePreviewing;
    private CancellationTokenSource? _todaySchedulePreviewCts;

    /// <summary>预览期间的课间轮换参数：轮换周期与「临近上课恢复原显示」阈值按课间真实时长等比缩小。</summary>
    private TimeSpan? _previewRotationPeriod;
    private TimeSpan? _previewNearThreshold;

    /// <summary>预览分段：模拟时间从 Start 匀速推进到 End，耗时 DurationMs；Item 为对应时间点（余量/空档段为 null）。</summary>
    private sealed class TodaySchedulePreviewSegment
    {
        public TimeSpan Start;
        public TimeSpan End;
        public double DurationMs;
        public double BeginMs;
        public TimeLayoutItem? Item;
    }

    /// <summary>是否正在快进预览今日时间表。</summary>
    public bool IsTodaySchedulePreviewing => _isTodaySchedulePreviewing;

    /// <summary>
    /// 快速预览今日时间表：每个时间点（上课/课间）各播放 10 秒，逐段推进模拟时间，
    /// 让主界面依次播放一天内所有上课、课间与放学状态及其切换动画，用于快速检查。
    /// </summary>
    /// <param name="progress">进度回调，包含 0~1 进度与状态文本。</param>
    /// <param name="cancellationToken">用于提前停止预览。</param>
    /// <returns>正常完成返回 null；无法开始或中途失败时返回提示文本。</returns>
    public async Task<string?> PlayTodaySchedulePreviewAsync(
        IProgress<TodaySchedulePreviewProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_isTodaySchedulePreviewing || _presenceBusy)
        {
            return "已有动画正在播放，请稍后再试。";
        }

        var settings = SettingsService.Settings;
        var now = ExactTimeService.GetCurrentLocalDateTime();
        var today = now.Date;
        var classPlan = LessonsService.CurrentClassPlan ?? LessonsService.GetClassPlanByDate(today);
        var items = classPlan?.ValidTimeLayoutItems is { } validItems
            ? validItems.Where(i => i.TimeType is 0 or 1).OrderBy(i => i.StartTime).ToList()
            : new List<TimeLayoutItem>();
        if (items.Count == 0)
        {
            return "今天没有可预览的时间表内容（未找到课程或课间时间点）。";
        }

        // 构建分段：上课前余量 3 秒 →（时间点间空档 2 秒）→ 每个时间点 10 秒 → 放学后余量 3 秒。
        // 保证模拟时间不跨天，避免课表被重新加载。
        var firstStart = items.First().StartTime;
        var lastEnd = items.Max(i => i.EndTime);
        if (lastEnd <= firstStart)
        {
            lastEnd = firstStart + TimeSpan.FromHours(1);
        }
        var from = firstStart - TimeSpan.FromSeconds(3);
        if (from < TimeSpan.Zero)
        {
            from = TimeSpan.Zero;
        }
        var to = lastEnd + TimeSpan.FromSeconds(3);
        var dayEnd = TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1);
        if (to > dayEnd)
        {
            to = dayEnd;
        }

        var segments = new List<TodaySchedulePreviewSegment>();
        var cursor = from;
        double cumMs = 0;
        void AddSegment(TimeSpan start, TimeSpan end, double durationMs, TimeLayoutItem? item)
        {
            if (durationMs <= 0)
            {
                return;
            }
            segments.Add(new TodaySchedulePreviewSegment
            {
                Start = start,
                End = end,
                DurationMs = durationMs,
                BeginMs = cumMs,
                Item = item
            });
            cumMs += durationMs;
        }

        AddSegment(from, firstStart, 3000, null); // 上课前
        foreach (var item in items)
        {
            var start = item.StartTime > cursor ? item.StartTime : cursor;
            var end = item.EndTime > start ? item.EndTime : start + TimeSpan.FromSeconds(1);
            if (start > cursor)
            {
                AddSegment(cursor, start, 2000, null); // 时间点之间的空档
            }
            AddSegment(start, end, TodaySchedulePreviewItemDuration.TotalMilliseconds, item);
            cursor = end;
        }
        if (to > cursor)
        {
            AddSegment(cursor, to, 3000, null); // 放学后
        }
        var totalMs = Math.Max(cumMs, 1);

        // 记录并稍后完整还原调试时间与通知设置。
        var originalOffset = settings.DebugTimeOffsetSeconds;
        var originalSpeed = settings.DebugTimeSpeed;
        var originalNotificationEnabled = settings.IsNotificationEnabled;
        // now 已包含 originalOffset；去掉它即为未加调试偏移的真实时间基准。
        var realBase = now - TimeSpan.FromSeconds(originalOffset);

        _isTodaySchedulePreviewing = true;
        _todaySchedulePreviewCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _todaySchedulePreviewCts.Token;

        try
        {
            settings.DebugTimeSpeed = 0;
            settings.IsNotificationEnabled = false;

            // 先把模拟时间推进到起始点，等课程服务按新时间重算状态后同步一次显示状态，
            // 之后的状态切换（上课收起/课间入场）随预览自动播放，便于检查动画。
            settings.DebugTimeOffsetSeconds = (today + from - realBase).TotalSeconds;
            await Task.Delay(200, ct);
            WelcomeOverlay.IsVisible = false;
            await ApplyPresenceAsync();

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report(new TodaySchedulePreviewProgress(1.0, "已停止预览，已恢复当前时间。"));
                    break;
                }

                var elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs >= totalMs)
                {
                    progress?.Report(new TodaySchedulePreviewProgress(1.0, "预览完成，已恢复当前时间。"));
                    break;
                }

                var segment = segments.Last(s => elapsedMs >= s.BeginMs);
                var localP = segment.DurationMs <= 0
                    ? 1.0
                    : Math.Min((elapsedMs - segment.BeginMs) / segment.DurationMs, 1.0);
                var simulated = segment.Start +
                    TimeSpan.FromTicks((long)((segment.End - segment.Start).Ticks * localP));
                var target = today + simulated;
                // 减去已流逝的真实时间，抵消 GetCurrentLocalDateTime() 中系统时间的自然增长。
                settings.DebugTimeOffsetSeconds = (target - realBase).TotalSeconds - elapsedMs / 1000.0;

                // 预览中的课间轮换参数：轮换周期取课间预览时长的 1/3（三个视图各显示一次），
                // 最后 1/6 恢复原显示（对应真实使用中临近上课最后几十秒）。
                if (segment.Item is { TimeType: 1 } && segment.End > segment.Start)
                {
                    _previewRotationPeriod = TimeSpan.FromTicks((segment.End - segment.Start).Ticks / 3);
                    _previewNearThreshold = TimeSpan.FromTicks((segment.End - segment.Start).Ticks / 6);
                }
                else
                {
                    _previewRotationPeriod = null;
                    _previewNearThreshold = null;
                }

                var p = Math.Min(elapsedMs / totalMs, 1.0);
                progress?.Report(new TodaySchedulePreviewProgress(p,
                    BuildTodaySchedulePreviewText(p, simulated, items, segment.Item)));

                try
                {
                    await Task.Delay(33, ct);
                }
                catch (TaskCanceledException)
                {
                    progress?.Report(new TodaySchedulePreviewProgress(1.0, "已停止预览，已恢复当前时间。"));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "快进预览今日时间表失败");
            return $"预览失败：{ex.Message}";
        }
        finally
        {
            settings.DebugTimeOffsetSeconds = originalOffset;
            settings.DebugTimeSpeed = originalSpeed;
            settings.IsNotificationEnabled = originalNotificationEnabled;
            _isTodaySchedulePreviewing = false;
            _previewRotationPeriod = null;
            _previewNearThreshold = null;
            _todaySchedulePreviewCts?.Dispose();
            _todaySchedulePreviewCts = null;
            // 等课程服务按恢复后的真实时间重算状态，再决定是否收起主界面。
            try
            {
                await Task.Delay(150);
                await ApplyPresenceAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "恢复主界面显示状态失败");
            }
        }
        return null;
    }

    /// <summary>提前停止今日时间表快进预览。</summary>
    public void StopTodaySchedulePreview()
    {
        try
        {
            _todaySchedulePreviewCts?.Cancel();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "停止预览失败");
        }
    }

    private string BuildTodaySchedulePreviewText(double percent, TimeSpan simulated,
        IReadOnlyList<TimeLayoutItem> items, TimeLayoutItem? currentItem)
    {
        var index = -1;
        if (currentItem != null)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], currentItem))
                {
                    index = i;
                    break;
                }
            }
        }
        var pointText = index >= 0 ? $" · 第 {index + 1}/{items.Count} 个时间点" : "";
        var subject = LessonsService.CurrentSubject?.Name;
        var stateText = LessonsService.CurrentState switch
        {
            TimeState.OnClass => string.IsNullOrWhiteSpace(subject) ? "上课" : $"上课 · {subject}",
            TimeState.Breaking => string.IsNullOrWhiteSpace(subject) ? "课间" : $"课间 · {subject}",
            TimeState.AfterSchool => "放学后",
            _ => "上课前 / 无课程"
        };
        return $"{percent * 100:0}% · 模拟时间 {simulated:hh\\:mm\\:ss} · {stateText}{pointText}";
    }

    #endregion

    #region 课间内容轮换（课表 → 距离上课时间 → 当前时间，临近上课恢复原显示）

    private string? _lastBreakRotationCountdownText;
    private string? _lastBreakRotationClockText;
    private string? _lastBreakRotationDateText;

    /// <summary>
    /// 课间轮换覆盖层当前是否正在显示（供行控件的鼠标悬停虚化判定使用）。
    /// 覆盖层显示时整块主界面都被它盖住，因此鼠标在窗口任意位置都应算作「在主界面上」。
    /// </summary>
    internal bool IsBreakRotationOverlayVisible =>
        BreakRotationOverlay is { IsVisible: true } && BreakRotationOverlay.Opacity > 0.01;

    /// <summary>
    /// 课间内容轮换：课间时主界面内容每 20 秒在 课表（原显示）→ 距离上课时间 → 当前时间 之间轮换；
    /// 距离上课只剩最后几十秒（CountdownSeconds 设置，默认 60 秒）时恢复原显示（课表）。
    /// 快进预览期间轮换周期与阈值按课间预览时长等比缩小，便于在预览中看到完整轮换。
    /// </summary>
    private void UpdateBreakRotation()
    {
        var showOverlay = false;
        string? countdownText = null;
        string? clockText = null;
        string? dateText = null;
        try
        {
            if (_entranceAnimationPlayed &&
                _isInterfacePresent &&
                !ViewModel.IsMainWindowCollapsed &&
                !_presenceBusy &&
                LessonsService.CurrentState == TimeState.Breaking &&
                LessonsService.CurrentTimeLayoutItem is { TimeType: 1 } breakItem)
            {
                var now = ExactTimeService.GetCurrentLocalDateTime();
                var elapsed = now.TimeOfDay - breakItem.StartTime;
                var remaining = LessonsService.OnClassLeftTime;
                var threshold = _isTodaySchedulePreviewing && _previewNearThreshold.HasValue
                    ? _previewNearThreshold.Value
                    : TimeSpan.FromSeconds(Math.Max(SettingsService.Settings.CountdownSeconds, 1));
                var period = _isTodaySchedulePreviewing && _previewRotationPeriod.HasValue
                    ? _previewRotationPeriod.Value
                    : TimeSpan.FromSeconds(20);
                if (period > TimeSpan.Zero && elapsed >= TimeSpan.Zero && remaining > threshold)
                {
                    // 相位：0=课表（原显示，隐藏覆盖层）1=距离上课时间 2=当前时间。
                    var phase = (int)(elapsed.Ticks / period.Ticks) % 3;
                    if (phase == 1)
                    {
                        showOverlay = true;
                        countdownText = $"距上课 {remaining:hh\\:mm\\:ss}";
                        var nextSubject = LessonsService.NextClassSubject?.Name;
                        if (!string.IsNullOrWhiteSpace(nextSubject))
                        {
                            countdownText += $" · 下节 {nextSubject}";
                        }
                    }
                    else if (phase == 2)
                    {
                        showOverlay = true;
                        clockText = now.ToString("HH:mm:ss");
                        var week = now.DayOfWeek switch
                        {
                            DayOfWeek.Monday => "星期一",
                            DayOfWeek.Tuesday => "星期二",
                            DayOfWeek.Wednesday => "星期三",
                            DayOfWeek.Thursday => "星期四",
                            DayOfWeek.Friday => "星期五",
                            DayOfWeek.Saturday => "星期六",
                            _ => "星期日"
                        };
                        dateText = $"{now:yyyy年M月d日} {week}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "更新课间内容轮换失败");
        }

        var targetOpacity = showOverlay ? (IsMainWindowMouseInFaded() ? 0.05 : 1d) : 0d;
        if (Math.Abs(BreakRotationOverlay.Opacity - targetOpacity) > 0.001)
        {
            BreakRotationOverlay.Opacity = targetOpacity;
        }
        SetBreakRotationViewActive(BreakRotationCountdownView, countdownText != null);
        SetBreakRotationViewActive(BreakRotationClockView, clockText != null);
        if (countdownText != null && countdownText != _lastBreakRotationCountdownText)
        {
            BreakRotationCountdownText.Text = countdownText;
            _lastBreakRotationCountdownText = countdownText;
        }
        if (clockText != null && (clockText != _lastBreakRotationClockText || dateText != _lastBreakRotationDateText))
        {
            BreakRotationClockText.Text = clockText;
            BreakRotationDateText.Text = dateText;
            _lastBreakRotationClockText = clockText;
            _lastBreakRotationDateText = dateText;
        }
    }

    /// <summary>
    /// 切换课间轮换视图：加 active 类触发淡入 + 上浮动画，去掉则淡出 + 下沉。
    /// </summary>
    private static void SetBreakRotationViewActive(Control view, bool active)
    {
        if (view.Classes.Contains("active") == active)
        {
            return;
        }
        view.Classes.Set("active", active);
    }

    /// <summary>
    /// 主界面当前是否处于「鼠标悬停虚化」状态（与主界面行的判定规则一致）。
    /// 轮换覆盖层不属于任何主界面行，需要单独跟随虚化，
    /// 否则鼠标移到轮换出来的内容上时主界面不会变淡。
    /// </summary>
    private bool IsMainWindowMouseInFaded()
    {
        var settings = SettingsService.Settings;
        return settings.IsMouseInFadingEnabled &&
               (_isPointerOverMainWindow ^ settings.IsMouseInFadingReversed);
    }

    #endregion
}
