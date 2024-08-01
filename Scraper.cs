using System.Text.RegularExpressions;
using HtmlAgilityPack;

public record Scraper(string BaseURL, string Password, string OutputDirectory) {
    public async Task Run() {
        Console.WriteLine($"Scraping images from {BaseURL}");

        var client = new HttpClient() {
            BaseAddress = new Uri(BaseURL)
        };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("famini-scraper", "1.0"));

        var mainPageContent = await client.GetAsync("/");
        mainPageContent.EnsureSuccessStatusCode();
        string content = await mainPageContent.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var internLink = doc.DocumentNode.Descendants("a").FirstOrDefault(a => a.InnerText.Contains("Intern"));

        if (internLink == null) {
            throw new Exception("Did not find the intern section");
        }

        var internURL = internLink.GetAttributeValue("href", String.Empty);

        var internResponseNotLogged = await client.GetAsync(internURL);

        // Funnily enough HTTP status is 200 OK

        string internContentNotLogged = await internResponseNotLogged.Content.ReadAsStringAsync();

        var loginDoc = new HtmlDocument();
        loginDoc.LoadHtml(internContentNotLogged);

        var loginForm = loginDoc.DocumentNode.Descendants("form").FirstOrDefault();

        if (loginForm == null) {
            throw new Exception("Did not find login form");
        }

        var formFields = loginForm.Descendants("input")
            .ToDictionary(input => input.GetAttributeValue("name", String.Empty), input => input.GetAttributeValue("value", String.Empty));

        formFields["password"] = Password;

        var postContent = new FormUrlEncodedContent(formFields);

        var postResponse = await client.PostAsync(loginForm.GetAttributeValue("action", String.Empty), postContent);

        string loggedInContent = await postResponse.Content.ReadAsStringAsync();

        var loggedInDoc = new HtmlDocument();
        loggedInDoc.LoadHtml(loggedInContent);

        var imagesThatWeCanLoad = loggedInDoc.DocumentNode.Descendants("a").Where(a =>
            !String.IsNullOrEmpty(a.GetAttributeValue("data-href", String.Empty))).ToList();

        var imagesToLoad = imagesThatWeCanLoad.Select(link => 
        EnsureNoTransform(link.GetAttributeValue("data-href", String.Empty))
        );

        if (!Directory.Exists(OutputDirectory)) {
            Directory.CreateDirectory(OutputDirectory);
        }

        foreach (var path in imagesToLoad) {
            await DownloadImage(client, path);
        }
    }

    private string EnsureNoTransform(string path) {
        if (path.Contains("trans/none")) {
            return path;
        }
        var m = _fNameRegex.Match(path);
        if (!m.Success) {
            throw new Exception("Could not match " + path);
        }
        return "https://" + m.Groups["cdnPath"].Value + "/app/cms/image/transf/none/path/s5c4ab2d9a0f14b44/image/" +
            m.Groups["imageName"].Value + "/version/" + m.Groups["versionName"].Value + "/image.jpg";
    }

    private Regex _fNameRegex = new Regex("https://(?<cdnPath>[^/]+).*image/(?<imageName>[^/]+)/version/(?<versionName>[^/]+)");

    private string NiceFileName(string path) {
        var m = _fNameRegex.Match(path);
        if (!m.Success) {
            throw new Exception("Couldn't match " + path);
        }
        string fileName = m.Groups["imageName"].Value + "_" + m.Groups["versionName"].Value + ".jpg";
        return fileName;
    }

    private async Task DownloadImage(HttpClient client, string path) {
        var content = await client.GetAsync(path);

        string outputPath = Path.Combine(OutputDirectory, NiceFileName(path));

        if (File.Exists(outputPath)) {
            File.Delete(outputPath);
        }

        var outputStream = await content.Content.ReadAsStreamAsync();

        using (var fs = File.Create(outputPath)) {
            await outputStream.CopyToAsync(fs);
        }
    }
}