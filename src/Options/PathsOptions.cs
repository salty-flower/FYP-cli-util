using System.Diagnostics.CodeAnalysis;

namespace DataCollection.Options;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class PathsOptions
{
    public string PaperMetadataDir { get; set; } = string.Empty;
    public string PdfDataDir { get; set; } = string.Empty;
    public string PaperBinDir { get; set; } = string.Empty;
    public string PythonDLL { get; set; } = string.Empty;
}
