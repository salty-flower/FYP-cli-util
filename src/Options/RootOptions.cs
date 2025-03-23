namespace DataCollection.Options;

public class RootOptions
{
    /// <summary>
    /// Job name, typically in the format 'conf-yyyy', e.g., 'icse-2024', 'issta-2021'
    /// </summary>
    public string JobName { get; set; } = "default";
}
