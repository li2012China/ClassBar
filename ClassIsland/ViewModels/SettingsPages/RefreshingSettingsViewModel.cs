using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public partial class RefreshingSettingsViewModel(
    SettingsService settingsService, 
    IRefreshingService refreshingService) : ObservableObject
{
    public SettingsService SettingsService { get; } = settingsService;
    public IRefreshingService RefreshingService { get; } = refreshingService;
}