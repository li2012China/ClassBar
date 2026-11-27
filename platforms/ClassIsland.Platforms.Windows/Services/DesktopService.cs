using ClassIsland.Core;
using ClassIsland.Platforms.Abstraction.Services;
using WindowsShortcutFactory;

namespace ClassIsland.Platform.Windows.Services;

public class DesktopService : IDesktopService
{
    private static string StartupShortcutName => "ClassBar.lnk";
    private static string LegacyStartupShortcutName => "ClassIsland.lnk";

    private static string StartupPath(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), name);

    public bool IsAutoStartEnabled
    {
        get => File.Exists(StartupPath(StartupShortcutName));
        set
        {
            var path = StartupPath(StartupShortcutName);
            // 清理旧版开机启动项，避免升级后仍指向 ClassIsland
            var legacy = StartupPath(LegacyStartupShortcutName);
            if (File.Exists(legacy))
            {
                File.Delete(legacy);
            }
            if (value)
            {
                using var shortcut = new WindowsShortcut();
                shortcut.Path = AppBase.ExecutingEntrance;
                shortcut.Arguments = "--autostartup";
                shortcut.WorkingDirectory = Path.GetDirectoryName(AppBase.ExecutingEntrance);
                shortcut.Save(path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
