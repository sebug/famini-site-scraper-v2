if (args.Length == 0) {
    Console.WriteLine("Usage: FaminiScraper outputDirectory");
    return;
}

string baseUrl = Environment.GetEnvironmentVariable("FAMINI_URL") ?? throw new Exception("Define env variable FAMINI_URL");
string password = Environment.GetEnvironmentVariable("FAMINI_PASSWORD") ?? throw new Exception("Define env variable FAMINI_PASSWORD");

var scraper = new Scraper(baseUrl, password, args[0]);

await scraper.Run();