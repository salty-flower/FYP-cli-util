namespace DataCollection.Options;

public partial class RootOptions
{
    /// <summary>
    /// Job name, typically in the format 'conf-yyyy', e.g., 'icse-2024', 'issta-2021'
    /// </summary>
    public required string JobName { get; set; }
}
