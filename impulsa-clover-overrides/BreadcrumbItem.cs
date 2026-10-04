namespace ImpulsaExplorer.Models;

public sealed class BreadcrumbItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public override string ToString() => Name;
}
