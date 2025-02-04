using System.Text.RegularExpressions;
using HtmlAgilityPack;

public record Scraper(string BaseURL, string Password, string OutputDirectory) {
    public async Task Run() {
        Console.WriteLine($"Scrapping images from {BaseURL}");

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

        var h1s = loggedInDoc.DocumentNode.Descendants("h1").ToList();
        var parentDivs = h1s.Select(h1 => h1.ParentNode).Skip(1).ToList(); // Skip 1 for the general information header
        var headerDivsWithPictureDivs = new List<PictureDivSection>();
        // associate the header div with the picture divs
        for (int i = 0; i < parentDivs.Count; i += 1)
        {
            var headerDiv = parentDivs[i];
            var stopDiv = i < parentDivs.Count - 1 ? parentDivs[i + 1] : null;
            var headerDivWithPictureDivs = new PictureDivSection(headerDiv.InnerText.Trim(),
            new List<HtmlNode>());
            headerDivsWithPictureDivs.Add(headerDivWithPictureDivs);
            var currentDiv = headerDiv.NextSibling;
            while (currentDiv != stopDiv && currentDiv != null)
            {
                headerDivWithPictureDivs.PictureDivs.Add(currentDiv);
                currentDiv = currentDiv.NextSibling;
            }
        }

        foreach (var headerDivWithPictureDivs in headerDivsWithPictureDivs)
        {
            string folderName = Path.Combine(OutputDirectory, headerDivWithPictureDivs.Header);
            if (!Directory.Exists(folderName))
            {
                Directory.CreateDirectory(folderName);
            }
            var imagesThatWeCanLoad = headerDivWithPictureDivs
            .PictureDivs.SelectMany(div => div.Descendants("a").Where(a =>
                !String.IsNullOrEmpty(a.GetAttributeValue("data-href", String.Empty)))).ToList();
            
            var imagesToLoad = imagesThatWeCanLoad.Select(link => 
                EnsureNoTransform(link.GetAttributeValue("data-href", String.Empty))
                ).ToList();
            Console.WriteLine("Download " + imagesToLoad.Count + " images for header " + 
            headerDivWithPictureDivs.Header);

            int i = 0;
            foreach (var image in imagesToLoad)
            {
                string fileName = image.Substring(image.LastIndexOf("/") + 1);
                string extension = Path.GetExtension(fileName);
                fileName = $"image_{i:D3}" + extension;
                await DownloadImage(client, image, Path.Combine(folderName, fileName));
                i += 1;
            }
        }





        // if (!Directory.Exists(OutputDirectory)) {
        //     Directory.CreateDirectory(OutputDirectory);
        // }

        // foreach (var path in imagesToLoad) {
        //     await DownloadImage(client, path);
        // }
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

    private async Task DownloadImage(HttpClient client, string url, string filePath) {
        var content = await client.GetAsync(url);

        var outputStream = await content.Content.ReadAsStreamAsync();

        using (var fs = File.Create(filePath)) {
            await outputStream.CopyToAsync(fs);
        }
    }
}