using System;
using Avalonia;
using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Views.SettingPages;

[Group("classisland.general")]
[FullWidthPage]
[SettingsPageInfo("profile", "档案", "\uE699", "\uE699", SettingsPageCategory.Internal)]
public partial class ProfileSettingsPage : SettingsPageBase
{
    public ProfileSettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ProfileView.Open(NavigationUri);
    }
}
