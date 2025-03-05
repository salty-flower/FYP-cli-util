using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataCollection;
using DataCollection.Models;
using DataCollection.Services;
using DataCollection.Utils;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Python.Runtime;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddHttpClient(
    "acm-scraper",
    c =>
    {
        c.BaseAddress = new Uri("https://dl-acm-org.libproxy1.nus.edu.sg");
        // cookie
        c.DefaultRequestHeaders.Add("Cookie", "ezproxy=zyAqRdlpXAU2pcc");
        c.DefaultRequestHeaders.Add(
            "Cookie",
            "utag_main=v_id:0194bb604749001b37f4992ab74605050004200d008f3$_sn:1$_se:64$_ss:0$_st:1738319683269$ses_id:1738310436681%3Bexp-session$_pn:31%3Bexp-session$vapi_domain:ieeexplore-ieee-org.libproxy1.nus.edu.sg"
        );
    }
);
builder.Services.AddSingleton<AcmScraper>();

var app = builder.Build();

var paperMetadataDir = new DirectoryInfo("../paper-metadata");
if (!paperMetadataDir.Exists)
    paperMetadataDir.Create();

var binaryDir = new DirectoryInfo("../pdfdata");
if (!binaryDir.Exists)
    binaryDir.Create();

if (true)
{
    using (var scope = app.Services.CreateScope())
    {
        var scraper = scope.ServiceProvider.GetRequiredService<AcmScraper>();

        var papers = await scraper.GetSectionPapers();
        foreach (var paper in papers)
        {
            var bin = MemoryPackSerializer.Serialize(paper);
            var binPath = Path.Combine(paperMetadataDir.FullName, $"{paper.SanitizedDoi}.bin");
            File.WriteAllBytes(binPath, bin);
        }
        await scraper.DownloadPapersAsync(papers, "../paper-bin");
    }

    Runtime.PythonDLL = "D:\\Scoop\\persist\\mambaforge\\envs\\daily\\python311.dll";
    PythonEngine.Initialize();
    PythonEngine.BeginAllowThreads();

    var filter = new Filter(new DirectoryInfo("../paper-bin"));

    // read dumped file


    //var pdfDataList = filter.FilterPapers();
    var alreadyDumped = binaryDir.GetFiles("*.bin").Select(f => f.Name.Replace(".bin", ""));
    await foreach (
        var d in filter.FilterPapersAsync(
            alreadyDumped.ToDictionary(p => p, _ => true).AsReadOnly()
        )
    )
    {
        Console.WriteLine($"Processing {d.FileName}");

        string[] keywords = ["bug", "develop", "acknowledge", "maintain", "confirm"];

        Console.Write("Keywords: ");
        foreach (var keyword in keywords)
        {
            var keywordCount = d.Texts.Sum(w => w.ToLower().CountSubstring(keyword));
            Console.Write($"{keyword} * {keywordCount}, ");
        }
        Console.WriteLine();

        var bin = MemoryPackSerializer.Serialize<PdfData>(d);
        var binPath = Path.Combine(binaryDir.FullName, $"{d.FileName}.bin");
        File.WriteAllBytes(binPath, bin);

        Console.WriteLine();
        Console.WriteLine();
    }
}

// Deserialize paper metadata, and check abstract
var validDois = new List<string>();
foreach (var d in paperMetadataDir.GetFiles("*.bin"))
{
    var bin = File.ReadAllBytes(d.FullName);
    var paper = MemoryPackSerializer.Deserialize<Paper>(bin);

    if (paper == null)
    {
        Console.WriteLine($"Null paper {d.Name}, skip");
        continue;
    }

    Console.Write($"Processing {paper.SanitizedDoi}... ");
    string[] mustExistKeywords = ["bug", "test", "confirm", "develop", "detect"];
    //// check if all keywords exist. if not, log and continue
    //if (!mustExistKeywords.All(k => paper.Abstract.Contains(k)))
    //{
    //    Console.WriteLine("Missing keyword, skipped");
    //    continue;
    //}
    //string[][] mustExistPairs = [["confirmed", "acknowledged"]];
    //// check for each pair, at least one keyword in the pair exists
    //if (!mustExistPairs.All(p => p.Any(k => paper.Abstract.Contains(k))))
    //{
    //    Console.WriteLine("Missing keyword pair, skipped");
    //    continue;
    //}
    //(string, ushort, ushort)[] occurrenceBoundedKeywords = [("bug", 1, ushort.MaxValue)];
    //// check for each keyword, the occurrence is within the bound
    //if (!occurrenceBoundedKeywords.All(k => paper.Abstract.ToLower().CountSubstring(k.Item1) >= k.Item2 && paper.Abstract.ToLower().CountSubstring(k.Item1) <= k.Item3))
    //{
    //    Console.WriteLine("Keyword occurrence out of bound, skipped");
    //    continue;
    //}
    //validDois.Add(paper.SanitizedDoi);
    Console.WriteLine("Keywords: ");
    foreach (var keyword in mustExistKeywords)
    {
        var keywordCount = paper.Abstract.ToLower().CountSubstring(keyword);
        Console.Write($"{keyword} * {keywordCount}, ");
    }
    Console.WriteLine();
}
Environment.Exit(0);

// Deserialize PDF data, and check
var validFiles = new List<string>();
foreach (var f in binaryDir.GetFiles("*.bin"))
{
    var bin = File.ReadAllBytes(f.FullName);
    var d = MemoryPackSerializer.Deserialize<PdfData>(bin);
    Console.Write($"Processing {d.FileName}... ");

    string[] mustExistKeywords = ["bug", "test", "confirm", "develop", "detect"];
    // check if all keywords exist. if not, log and continue
    if (mustExistKeywords.Any(k => !d.Texts.Any(t => t.Contains(k))))
    {
        Console.WriteLine("Missing keyword, skipped");
        continue;
    }

    string[][] mustExistPairs =
    [
        ["confirmed", "acknowledged"],
    ];
    // check for each pair, at least one keyword in the pair exists
    if (mustExistPairs.Any(p => !p.Any(k => d.Texts.Any(t => t.Contains(k)))))
    {
        Console.WriteLine("Missing keyword pair, skipped");
        continue;
    }

    (string, ushort, ushort)[] occurrenceBoundedKeywords = [("bug", 1, ushort.MaxValue)];
    // check for each keyword, the occurrence is within the bound
    if (
        occurrenceBoundedKeywords.Any(k =>
            d.Texts.Sum(w => w.ToLower().CountSubstring(k.Item1)) < k.Item2
            || d.Texts.Sum(w => w.ToLower().CountSubstring(k.Item1)) > k.Item3
        )
    )
    {
        Console.WriteLine("Keyword occurrence out of bound, skipped");
        continue;
    }

    validFiles.Add(f.Name.Replace(".bin", ""));

    string[] keywords =
    [
        "bug",
        "develop",
        "acknowledge",
        "maintain",
        "confirm",
        "confirmed",
        "acknowledged",
        "detect",
    ];
    Console.Write("Keywords: ");
    foreach (var keyword in keywords)
    {
        var keywordCount = d.Texts.Sum(w => w.ToLower().CountSubstring(keyword));
        Console.Write($"{keyword} * {keywordCount}, ");
    }
    Console.WriteLine();
}
Console.WriteLine("All filtered papers this round:");
Console.WriteLine(string.Join(Environment.NewLine, validFiles));

Console.WriteLine("Both:");
Console.WriteLine(
    string.Join(
        Environment.NewLine,
        validFiles
            .Select(f => f.Replace(".pdf", "").Replace("-", "/"))
            .Where(f => Constants.ACTUAL_CURATED_DOIS_DICT.ContainsKey(f))
    )
);

Console.WriteLine("Not in actual curated DOIs:");
Console.WriteLine(
    string.Join(
        Environment.NewLine,
        validFiles
            .Select(f => f.Replace(".pdf", "").Replace("-", "/"))
            .Where(f => !Constants.ACTUAL_CURATED_DOIS_DICT.ContainsKey(f))
    )
);

Console.WriteLine("In actual curated DOIs but not here:");
Console.WriteLine(
    string.Join(
        Environment.NewLine,
        Constants.ACTUAL_CURATED_DOIS_DICT.Keys.Where(f =>
            !validFiles.Contains(f.Replace("/", "-") + ".pdf")
        )
    )
);
System.Environment.Exit(0);
