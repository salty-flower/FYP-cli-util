using System;
using DataCollection.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddHttpClient(
    "acm-scraper",
    c =>
    {
        c.BaseAddress = new Uri("https://dl-acm-org.libproxy1.nus.edu.sg");
        // cookie
        c.DefaultRequestHeaders.Add("Cookie", "ezproxy=0a4eo4ptWdqpk8A");
        c.DefaultRequestHeaders.Add(
            "Cookie",
            "utag_main=v_id:0194bb604749001b37f4992ab74605050004200d008f3$_sn:1$_se:64$_ss:0$_st:1738319683269$ses_id:1738310436681%3Bexp-session$_pn:31%3Bexp-session$vapi_domain:ieeexplore-ieee-org.libproxy1.nus.edu.sg"
        );
    }
);
builder.Services.AddSingleton<AcmScraper>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var scraper = scope.ServiceProvider.GetRequiredService<AcmScraper>();

    var papers = await scraper.GetSectionPapers();
    await scraper.DownloadPapersAsync(papers, "../paper-bin");
}

