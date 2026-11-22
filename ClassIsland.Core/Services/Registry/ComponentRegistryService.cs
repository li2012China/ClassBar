using System.Collections.ObjectModel;
using System.Reflection.Metadata;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Core.Services.Registry;

/// <summary>
/// 组件注册服务
/// </summary>
public class ComponentRegistryService
{
    /// <summary>
    /// 已注册的组件
    /// </summary>
    public static ObservableCollection<ComponentInfo> Registered { get; } = new();

    public static ObservableCollection<ComponentSettings> RegisteredSettings { get; } = new();

    public static Dictionary<Guid, Guid> MigrationPairs { get; } = new();

    /// <summary>
    /// 获取内置组件分组。
    /// </summary>
    public static IReadOnlyList<ComponentGroup> GetGroupedSortedComponents()
    {
        if (Registered.Count == 0)
        {
            return [];
        }

        return
        [
            new ComponentGroup(
                GroupName: "内置",
                Components: Registered.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList())
        ];
    }
}