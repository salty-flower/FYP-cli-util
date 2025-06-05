namespace DataCollection.Models.GitHub;

public record RepositoryTree
{
    public string? Sha { get; set; }
    public string? Url { get; set; }
    public List<TreeItem>? Tree { get; set; }
    public bool Truncated { get; set; }

    public record TreeItem
    {
        public string? Path { get; set; }
        public string? Mode { get; set; }
        public string? Type { get; set; }
        public string? Sha { get; set; }
        public string? Url { get; set; }
        public int? Size { get; set; }
    }
}
