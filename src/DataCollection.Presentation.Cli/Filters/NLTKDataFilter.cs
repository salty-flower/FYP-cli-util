using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Python.Runtime;

namespace DataCollection.Presentation.Cli.Filters;

internal class NLTKDataFilter(ConsoleAppFilter next, ILogger<NLTKDataFilter> logger)
    : ConsoleAppFilter(next)
{
    public static readonly string[] REQUIRED_NLTK_DATA =
    [
        "tokenizers/punkt_tab",
        "tokenizers/punkt",
        "taggers/averaged_perceptron_tagger_eng",
    ];

    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        using (Py.GIL())
        {
            dynamic nltk = Py.Import("nltk");

            // Check if all required NLTK data is installed
            var missingData = new List<string>();

            foreach (var data in REQUIRED_NLTK_DATA)
            {
                try
                {
                    nltk.data.find(data);
                }
                catch (PythonException e)
                {
                    if (
                        e.Message.Contains("Please use the NLTK Downloader to obtain the resource:")
                    )
                        missingData.Add(data);
                    else
                        logger.LogError(e, "Unknown error while checking NLTK data");
                }
            }

            if (missingData.Count > 0)
            {
                var downloadDir = Path.Combine(
                    Directory.GetParent(Runtime.PythonDLL!)!.FullName,
                    "nltk_data"
                );
                logger.LogInformation(
                    "Downloading missing NLTK data packages {packages} to {path}",
                    string.Join(", ", missingData),
                    downloadDir
                );
                foreach (var data in missingData)
                    nltk.download(data.Split("/").Last(), download_dir: downloadDir);
            }
        }

        await Next.InvokeAsync(context, cancellationToken);
    }
}
