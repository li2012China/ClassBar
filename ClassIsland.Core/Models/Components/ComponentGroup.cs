using ClassIsland.Core.Attributes;

namespace ClassIsland.Core.Models.Components;

/// <summary>
/// 组件库分组。当前所有组件均为内置组件。
/// </summary>
public record ComponentGroup(
    string GroupName,
    IReadOnlyList<ComponentInfo> Components);
