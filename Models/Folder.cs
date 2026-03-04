namespace Clean_Hackus_NET8.Models;

public class Folder
{
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }

    public Folder() { }

    public Folder(string name)
    {
        Name = name;
        IsEnabled = true;
    }
}
