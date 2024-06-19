namespace FluentCMS.Models;

public class Menu
{
    public string Icon { get; set; } = "";
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsHref { get; set; } = false;
}