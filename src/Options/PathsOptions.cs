namespace DataCollection.Options;

public class PathsOptions
{
    public string PaperMetadataDir { get; set; } = "../paper-metadata";
    public string PdfDataDir { get; set; } = "../pdfdata";
    public string PaperBinDir { get; set; } = "../paper-bin";
    public string PythonDLL { get; set; } = string.Empty;
}
