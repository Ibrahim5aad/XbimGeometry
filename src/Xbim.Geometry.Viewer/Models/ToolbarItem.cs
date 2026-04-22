using Microsoft.AspNetCore.Components;

namespace Xbim.Geometry.Viewer;

public abstract class ToolbarItemBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Icon { get; set; }
    public string? Tooltip { get; set; }
    public bool Disabled { get; set; }
    public string? CssClass { get; set; }
}

public class ToolbarButton : ToolbarItemBase
{
    public EventCallback OnClick { get; set; }
}

public class ToolbarToggle : ToolbarItemBase
{
    public bool IsActive { get; set; }
    public EventCallback<bool> OnToggle { get; set; }
    public string? ActiveIcon { get; set; }
    public string? ActiveTooltip { get; set; }
}

public class ToolbarDropdown : ToolbarItemBase
{
    public string? Label { get; set; }
    public List<ToolbarDropdownItem> Items { get; set; } = new();
}

public class ToolbarDropdownItem
{
    public string? Icon { get; set; }
    public string? Label { get; set; }
    public bool Disabled { get; set; }
    public EventCallback OnClick { get; set; }
}

public class ToolbarGroup : ToolbarItemBase
{
    public string? Label { get; set; }
    public List<ToolbarItemBase> Items { get; set; } = new();
}

public class ToolbarSeparator : ToolbarItemBase { }
