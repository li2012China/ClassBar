using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Models.Refreshing;
using ClassIsland.Platforms.Abstraction;
using ClassIsland.Platforms.Abstraction.Models;
using ClassIsland.Shared;
using ClassIsland.Shared.Helpers;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.Views;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.Services;

public class RefreshingService(SettingsService settingsService, IExactTimeService exactTimeService) : IRefreshingService
{
    public const string DefaultOnboardingToastTitle = "欢迎使用 ClassIsland (/≧▽≦)/";

    public const string DefaultOnboardingToastBody =
        "屏幕上的课表是由本大屏课表软件 ClassIsland 显示的。ClassIsland 能一目了然地显示各种信息，并支持高度自定义。要了解如何使用 ClassIsland 吗？";

    public const string DefaultRefreshingToastTitle = "翻新 ClassIsland";

    public const string DefaultRefreshingToastBody = "ClassIsland 似乎已经有一个长假没有启动了，部分设置可能已经过期。要跟随欢迎向导重新设置如学期开始时间等设置吗？";
    
    public SettingsService SettingsService { get; } = settingsService;
    public IExactTimeService ExactTimeService { get; } = exactTimeService;

    public async Task<bool> Initialize()
    {
        var now = ExactTimeService.GetCurrentLocalDateTime();
        var lastStartup = SettingsService.Settings.AppLastStartedTime;
        SettingsService.Settings.AppLastStartedTime = now;

        if ((now.Date - lastStartup.Date).TotalDays >= SettingsService.Settings.RefreshingToastThresholdDays || SettingsService.Settings.ShowRefreshingToastOnNextStart)
        {
            SettingsService.Settings.ShowRefreshingToastOnNextStart = false;
            SettingsService.Settings.LeftRefreshingToastCounts = SettingsService.Settings.MaxRefreshingToastCounts;
        }

        if (!SettingsService.Settings.IsRefreshingToastEnabled || SettingsService.Settings.LeftRefreshingToastCounts <= 0)
        {
            SettingsService.Settings.LeftRefreshingToastCounts = 0;
            return false;
        }

        SettingsService.Settings.LeftRefreshingToastCounts--;
        if (SettingsService.Settings.RefreshingToastIsOnboardingGuide)
        {
            if (SettingsService.Settings.LeftRefreshingToastCounts <= 0)
            {
                SettingsService.Settings.RefreshingToastIsOnboardingGuide = false;
            }
            return await ShowOnboardingDialog();
        }

        await ShowRefreshingToast();

        return false;
    }

    private async Task ShowRefreshingToast()
    {
        await PlatformServices.DesktopToastService.ShowToastAsync(new DesktopToastContent()
        {
            Title = DefaultRefreshingToastTitle,
            Body = DefaultRefreshingToastBody,
            Buttons =
            {
                ["暂时不用"] = () => { },
                ["立即翻新"] = async void () => await BeginRefresh(),
            }
        });
    }

    public async Task<bool> ShowOnboardingDialog(bool isTest=false)
    {
        var r = await new TaskDialog()
        {
            Header = SettingsService.Settings.OnboardingToastTitle,
            Content = SettingsService.Settings.OnboardingToastBody,
            Buttons =
            [
                new TaskDialogButton("退出并不再显示", 0),
                new TaskDialogButton("以后再说", 1),
                new TaskDialogButton("好",2)
                {
                    IsDefault = true
                },
            ],
            XamlRoot = AppBase.Current.GetRootWindow(),
            Title = "欢迎使用 ClassIsland"
        }.ShowAsync();
        if (Equals(r, 0))
        {
            PlatformServices.DesktopService.IsAutoStartEnabled = false;
            SettingsService.Settings.LeftRefreshingToastCounts = 0;
            SettingsService.Settings.RefreshingToastIsOnboardingGuide = false;
            AppBase.Current.Stop();
            return true;
        }
        if (!Equals(r, 2))
        {
            return false;
        }
        
        return await BeginRefresh(true);
    }

    public async Task<bool> BeginRefresh(bool isOnboarding=false)
    {
        var scopes = ConfigureFileHelper.CopyObject(SettingsService.Settings.RefreshingScopes);
        var dialog = new RefreshingScopesConfigDialog() { Scopes = scopes };
        await dialog.ShowDialog((Window)AppBase.Current.GetRootWindow());

        var profile = IAppHost.GetService<IProfileService>().Profile;
        await FileFolderService.CreateBackupAsync();

        if (!scopes.Profile)
        {
            if (scopes.ProfileClassPlans)
            {
                foreach (var (key, _) in profile.ClassPlans.Where(x => !scopes.ReservedClassPlans.Contains(x.Key))
                             .ToDictionary())
                {
                    profile.ClassPlans.Remove(key);
                }
            }

            var requiredTimeLayouts = profile.ClassPlans.Select(x => x.Value.TimeLayoutId).ToHashSet();
            if (scopes.ProfileTimeLayouts)
            {
                foreach (var (key, _) in profile.TimeLayouts
                             .Where(x => !scopes.ReservedTimeLayouts.Contains(x.Key)
                                         && !requiredTimeLayouts.Contains(x.Key))
                             .ToDictionary())
                {
                    profile.TimeLayouts.Remove(key);
                }
            }
        }
        else
        {
            var profileNew = new Profile();
            using var streamReader = new StreamReader(AssetLoader.Open(
                new Uri("avares://ClassIsland/Assets/default-subjects.json", UriKind.Absolute)));
            var subject = await streamReader.ReadToEndAsync();
            profile.Subjects = JsonSerializer.Deserialize<Profile>(subject)!.Subjects;
            var json = JsonSerializer.Serialize(profileNew);
            var newFile = Guid.NewGuid().ToString();
            await File.WriteAllTextAsync(Path.Combine(ProfileService.ProfilePath, $"{newFile}.json"), json);
            SettingsService.Settings.SelectedProfile = $"{newFile}.json";
        }

        if (scopes.Components)
        {
            var newName = Guid.NewGuid().ToString();
            var path = Path.Combine(ComponentsService.ComponentSettingsPath, newName + ".json");
            ConfigureFileHelper.SaveConfig(path, ComponentsService.DefaultComponentProfile);
            IAppHost.GetService<IComponentsService>().RefreshConfigs();
            SettingsService.Settings.CurrentComponentConfig = newName;
        }

        if (scopes.Automations)
        {
            var newName = Guid.NewGuid().ToString();
            var path = Path.Combine(AutomationService.AutomationConfigsFolderPath, newName + ".json");
            ConfigureFileHelper.SaveConfig(path, new ObservableCollection<Workflow>());
            IAppHost.GetService<IAutomationService>().RefreshConfigs();
            SettingsService.Settings.CurrentAutomationConfig = newName;
        }

        if (!scopes.Profile)
        {
            await PlatformServices.DesktopToastService.ShowToastAsync("翻新成功", "已按所选范围重置 ClassIsland 数据。");
            return false;
        }

        List<string> args = ["--refreshing", "-m"];
        if (isOnboarding)
        {
            args.Add("--onboarding");
        }
        AppBase.Current.Restart(args.ToArray());
        return isOnboarding;
    }
}