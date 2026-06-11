namespace LlamaServerLauncher.Models;

public class BrowserInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    public override string ToString() => Name;
}
