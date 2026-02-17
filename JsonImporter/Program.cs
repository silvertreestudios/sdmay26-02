using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using JsonImporter;
using Newtonsoft.Json.Linq;

namespace JsonImporter
{
    class Program
    {
        /*
        TLDR: What this program does:
        - Connects to GitHub API for the foundryvtt/pf2e repository (using a personal access token)
        - Recursively lists JSON files from the whitelisted directories/files (that are within 'targetDir')
        - Downloads each located JSON file
        - Checks if the JSON content meets specified criteria in IsContentApproved
        - Process JSON files based on their source directory (and other factores as needed)
        - Saves processed JSON files to designated local directory, maintaining hierarchy
        */

        static async Task Main(string[] args)
        {

            // get GitHub token from gitToken.txt in current directory
            string? tokenPath = Path.Combine(Directory.GetCurrentDirectory(), "gitToken.txt");
            try
            {
                var raw = await File.ReadAllTextAsync(tokenPath);
                Constants.token = (raw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(Constants.token))
                {
                    Console.WriteLine($"Error: {tokenPath} is empty. Please add your GitHub token to the file.");
                    return;
                }
                Console.WriteLine($"Loaded token from: {tokenPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading token file: {ex.Message}");
                return;
            }

            // create a single HttpClient configured for GitHub API and pass it to the parser
            using (var httpClient = CreateGitHubHttpClient())
            {
                var processingFunctions = new Dictionary<string, Func<string, string>>
                {
                    { "packs/ancestries", JsonProcessingFunctions.ProcessAncestryJson },
                    { "packs/heritages", JsonProcessingFunctions.ProcessHeritageJson },
                    { "packs/backgrounds", JsonProcessingFunctions.ProcessBackgroundJson },
                    { "packs/classes", JsonProcessingFunctions.ProcessClassJson },
                    { "packs/feats", JsonProcessingFunctions.ProcessFeatJson },
                    { "packs/spells", JsonProcessingFunctions.ProcessSpellJson },
                    { "packs/equipment", JsonProcessingFunctions.ProcessEquipmentJson },
                    { "packs/pathfinder-monster-core", JsonProcessingFunctions.ProcessMonsterJson },
                    { "packs/iconics", JsonProcessingFunctions.ProcessMonsterJson   } //TODO confirm if converts properly
                };
                var parser = new JSONParser(Constants.whitelist, processingFunctions, httpClient);
                await parser.SyncJsonFilesAsync(Constants.localRoot);
            }

            Console.WriteLine("Sync complete.");
        }

        // Create and configure an HttpClient for GitHub API calls
        private static HttpClient CreateGitHubHttpClient()
        {
            var http = new HttpClient();
            // GitHub requires a User-Agent
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JsonImporter");
            // Prefer the v3 media type explicitly
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
            // Use 'token' scheme (GitHub docs frequently show 'token <PAT>')
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("token", Constants.token);
            return http;
        }
    }

    public static class Constants
    {
        // GitHub API root for the pf2e repository contents
        public const string apiRoot = "https://api.github.com/repos/foundryvtt/pf2e/contents/";

        // Human-friendly branch label (kept for reference)
        public const string apiBranch = "v13-dev";

        // The Contents API accepts branch, tag, or commit SHA in the `ref` parameter — default tag "7.8.0".
        // !! WARNING !! Considerable changes made to source repo format after "7.8.0" that would require multiple changes to adjust for
        public const string apiRef = "7.8.0";

        // Specify directory within the apiRoot to limit scope of files read
        // WARNING: GitHub API limits easily hit when reading many files at once, use this to limit scope
        public const string targetDir = "packs";

        // Local root directory to save imported files.
        public static readonly string localRoot = Path.Combine(ComputeLocalRoot(), "Assets", "DataFiles");

        // GitHub Personal Access Token is now loaded at runtime from gitToken.txt (populated into this field by Program.Main)
        public static string token = "";

        // Whitelist of directories/files to copy, comment/uncomment directories as needed
        // WARNING: /equipment/ is extremely large with no sub directories, use with caution
        public static readonly HashSet<string> whitelist = new HashSet<string>
        {
            // "packs/spells/cantrip/",
            // "packs/spells/1st-rank/",
            "packs/equipment/longsword.json",
            "packs/equipment/scimitar.json",
            "packs/equipment/dogslicer.json",
            "packs/equipment/shortbow.json",
            "packs/equipment/sling.json",
            "packs/equipment/spear.json",
            "packs/equipment/halberd.json",
            "packs/equipment/leather-armor.json",
            "packs/equipment/breastplate.json",
            "packs/equipment/padded-armor.json",
            "packs/pathfinder-monster-core/goblin-warrior.json",
            "packs/pathfinder-monster-core/zombie-shambler.json",
            "packs/pathfinder-monster-core/skeleton-guard.json",
            "packs/pathfinder-monster-core/kobold-warrior.json",
            // "packs/ancestries/human.json",
            // "packs/heritages/human/skilled-human.json",
            // "packs/feats/ancestry/human/natural-skill.json",
            // "packs/backgrounds/warrior.json",
            // "packs/backgrounds/nomad.json",
            // "packs/classes/fighter.json",
            "packs/classes/barbarian.json",
            // "packs/iconics/valeros-level-1.json",
            // "packs/feats/class/shared-class-feats/reactive-shield.json"
        };

        public const bool requireRemaster = true; // or false, as needed
        public static readonly HashSet<string> sourceBooks = new HashSet<string>
        {
            "Pathfinder Player Core",
            "Pathfinder Player Core 2",
            "Pathfinder Monster Core",
            // "Pathfinder Core Rulebook", // TODO temp for importing iconic characters
            // "Pathfinder GM Core", // TODO temp for importing iconic characters
            // Add other allowed titles here
        };

        // Determine the repo root directory for saving files
        private static string ComputeLocalRoot()
        {
            string[] starts = new[]
            {
                AppContext.BaseDirectory ?? string.Empty,
                Directory.GetCurrentDirectory(),
                Environment.CurrentDirectory,
                AppDomain.CurrentDomain.BaseDirectory ?? string.Empty
            };

            foreach (var s in starts)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                try
                {
                    var dir = Path.GetFullPath(s);
                    DirectoryInfo cur = new DirectoryInfo(dir);
                    while (cur != null)
                    {
                        // Case A: the current folder is named "JsonImporter" -> return its parent as the repo root (<X>)
                        if (string.Equals(cur.Name, "JsonImporter", StringComparison.OrdinalIgnoreCase))
                        {
                            var parent = cur.Parent;
                            if (parent != null)
                                return parent.FullName;
                            break;
                        }

                        // Case B: the current folder contains a "JsonImporter" subfolder -> current folder is the repo root (<X>)
                        if (Directory.Exists(Path.Combine(cur.FullName, "JsonImporter")))
                        {
                            return cur.FullName;
                        }

                        cur = cur.Parent;
                    }
                }
                catch
                {
                    // ignore and try next candidate
                }
            }
            // Fallback: use the current directory as the repo root
            return Directory.GetCurrentDirectory();
        }
    }

    public class JSONParser
    {
        // Whitelist of directories/files to copy
        private HashSet<string> whitelist;

        // Map source directory to processing function
        private Dictionary<string, Func<string, string>> processingFunctions;

        // HttpClient reused for all GitHub requests (injected)
        private readonly HttpClient httpClient;

        // Constructor
        public JSONParser(HashSet<string> whitelist, Dictionary<string, Func<string, string>> processingFunctions, HttpClient httpClient)
        {
            this.whitelist = whitelist;
            this.processingFunctions = processingFunctions;
            this.httpClient = httpClient;
        }

        // Entry point: sync JSON files from GitHub to local
        public async Task SyncJsonFilesAsync(string localRootPath)
        {
            List<string> allFiles = await ListGitHubFilesAsync();
            Console.WriteLine($"Found {allFiles.Count} files to process");

            int rejectedCount = 0; // Track rejected files

            int maxDegreeOfParallelism = 8; // Adjust as needed
            using (var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism))
            {
                var tasks = allFiles.Select(async filePath =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Check whitelist: only process files in whitelisted directories
                        bool isWhitelisted = false;
                        foreach (var allowed in whitelist)
                        {
                            if (filePath.Replace("\\", "/").StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                            {
                                isWhitelisted = true;
                                break;
                            }
                        }
                        if (!isWhitelisted)
                            return;

                        // Download JSON file
                        string fileUrl = $"{Constants.apiRoot}{filePath}".Replace("\\", "/") + $"?ref={Constants.apiRef}";
                        string jsonContent = await DownloadJsonAsync(fileUrl);

                        // License check
                        if (!IsContentApproved(jsonContent))
                        {
                            System.Threading.Interlocked.Increment(ref rejectedCount);
                            return;
                        }

                        // Determine source directory for processing function
                        string sourceDir = Path.GetDirectoryName(filePath).Replace("\\", "/");
                        string processedJson = ProcessJson(sourceDir, jsonContent);

                        // Build local path, maintaining hierarchy
                        string relativePath = filePath.StartsWith("packs/") ? filePath.Substring("packs/".Length) : filePath;
                        string localPath = Path.Combine(localRootPath, relativePath);
                        await SaveJsonAsync(localPath, processedJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            Console.WriteLine($"Total files rejected by IsContentApproved: {rejectedCount}");
        }

        // Download JSON file from GitHub
        private async Task<string> DownloadJsonAsync(string fileUrl)
        {
            var response = await httpClient.GetAsync(fileUrl);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download: {fileUrl} (Status: {response.StatusCode})");
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Response body: {body}");
                throw new Exception($"Failed to download JSON file: {fileUrl} (Status: {response.StatusCode})");
            }

            var json = await response.Content.ReadAsStringAsync();
            dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            // The file content is in the "content" property, base64-encoded
            if (obj?.content != null)
            {
                string base64 = ((string)obj.content).Replace("\n", "");
                byte[] data = Convert.FromBase64String(base64);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            else
            {
                // Sometimes the response might not include "content" (unexpected); log full response for debugging
                Console.WriteLine($"No content field in API response for {fileUrl}. Full response: {json}");
                throw new Exception($"No content found in API response for {fileUrl}");
            }
        }

        // Process JSON content using the appropriate function
        private string ProcessJson(string sourceDir, string jsonContent)
        {
            string dir = sourceDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (processingFunctions != null && processingFunctions.TryGetValue(dir, out var processFunc))
                {
                    // Optionally: Ensure the JSON is valid before processing
                    try
                    {
                        // Attempt to deserialize to a dynamic object to check validity
                        var obj = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);
                        // Pass the original JSON to the processing function
                        string processedJson = processFunc(jsonContent);

                        // Optionally: Validate the processed JSON as well
                        var processedObj = JToken.Parse(processedJson);

                        string finalJson;
                        if (processedObj is JObject jObj)
                        {
                            jObj["Source"] = sourceDir;
                            finalJson = jObj.ToString(Newtonsoft.Json.Formatting.Indented);
                        }
                        else
                        {
                            // Do not attempt to add Source to non-object token; just pretty-print
                            finalJson = processedObj.ToString(Newtonsoft.Json.Formatting.Indented);
                        }

                        Newtonsoft.Json.JsonConvert.DeserializeObject(finalJson); // Validate

                        return finalJson;
                    }
                    catch (Newtonsoft.Json.JsonException ex)
                    {
                        throw new Exception($"Invalid JSON in {sourceDir}: {ex.Message}");
                    }
                }
                // Move up one directory
                int lastSlash = dir.LastIndexOf('/');
                if (lastSlash == -1) break;
                dir = dir.Substring(0, lastSlash);
            }
            // No processing function, but still validate JSON
            try
            {
                Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);
                return jsonContent;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new Exception($"Invalid JSON in {sourceDir}: {ex.Message}");
            }
        }

        // Save processed JSON to local directory, maintaining hierarchy
        private async Task SaveJsonAsync(string localPath, string jsonContent)
        {
            string? directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(localPath, jsonContent);
            Console.WriteLine($"Attempting to write file: {localPath}");
        }

        // Utility: Recursively list files in GitHub directory
        private async Task<List<string>> ListGitHubFilesAsync()
        {
            var files = new HashSet<string>();

            foreach (var allowed in whitelist)
            {
                string allowedNorm = allowed.Replace("\\", "/");
                if (allowedNorm.StartsWith(Constants.targetDir + "/", StringComparison.OrdinalIgnoreCase) ||
                    allowedNorm.Equals(Constants.targetDir, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedNorm.EndsWith("/"))
                    {
                        await ListGitHubFilesRecursiveAsync(allowedNorm.TrimEnd('/'), files);
                    }
                    else
                    {
                        // include ref (now a tag) for existence check
                        string apiUrl = $"{Constants.apiRoot}{allowedNorm}?ref={Constants.apiRef}";
                        var response = await httpClient.GetAsync(apiUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            files.Add(allowedNorm);
                        }
                        else
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"ListGitHubFilesAsync: GET failed for {apiUrl} (Status: {response.StatusCode})");
                            Console.WriteLine($"Response body: {body}");
                        }
                    }
                }
            }

            return new List<string>(files);
        }

        private async Task ListGitHubFilesRecursiveAsync(string relativePath, HashSet<string> files)
        {
            // include ref (tag) when listing directory contents
            string apiUrl = $"{Constants.apiRoot}{relativePath}?ref={Constants.apiRef}";
            Console.WriteLine($"Listing GitHub directory: {apiUrl}");
            var response = await httpClient.GetAsync(apiUrl);

            if (response.Headers.Contains("X-RateLimit-Remaining"))
            {
                var remaining = response.Headers.GetValues("X-RateLimit-Remaining");
                var reset = response.Headers.GetValues("X-RateLimit-Reset");
                string resetStr = string.Join(",", reset);
                if (long.TryParse(resetStr, out long resetUnix))
                {
                    var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).ToLocalTime();
                    Console.WriteLine($"GitHub API rate limit remaining: {string.Join(",", remaining)} resets at: {resetTime} (local time)");
                }
                else
                {
                    Console.WriteLine($"GitHub API rate limit remaining: {string.Join(",", remaining)} resets at: {resetStr} (Unix timestamp)");
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Failed to list directory: {apiUrl} (Status: {response.StatusCode})");
                Console.WriteLine($"Response body: {body}");
                Console.WriteLine("Check that the ref (tag or branch name) and path are correct; the API is case-sensitive and ref must exist.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            dynamic items = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            if (!(items is Newtonsoft.Json.Linq.JArray) || items.Count == 0)
                return;

            foreach (var item in items)
            {
                string type = (string)item.type;
                string name = (string)item.name;
                string path = (string)item.path;

                if (type == "file" && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(path.Replace("\\", "/"));
                }
                else if (type == "dir")
                {
                    string nextPath = ((string)item.path).Replace("\\", "/");
                    await ListGitHubFilesRecursiveAsync(nextPath, files);
                }
            }
        }

        // Check if JSON content has approved license (ORC)
        private bool IsContentApproved(string jsonContent)
        {
            try
            {
                var jsonObj = JToken.Parse(jsonContent);

                bool IsValidLicense(string? license) =>
                    license != null && (license.Equals("ORC", StringComparison.OrdinalIgnoreCase) );
                    // || license.Equals("OGL", StringComparison.OrdinalIgnoreCase));

                bool IsValidRemaster(JToken publication) =>
                    !Constants.requireRemaster || (publication["remaster"]?.Value<bool>() == true);

                // Collect all publication objects
                List<JToken> allPublications = new List<JToken>();
                void CollectPublications(JToken token)
                {
                    if (token.Type == JTokenType.Object)
                    {
                        var obj = (JObject)token;
                        var publication = obj["publication"];
                        if (publication != null)
                        {
                            allPublications.Add(publication);
                        }
                        foreach (var property in obj.Properties())
                        {
                            CollectPublications(property.Value);
                        }
                    }
                    else if (token.Type == JTokenType.Array)
                    {
                        foreach (var item in token)
                        {
                            CollectPublications(item);
                        }
                    }
                }

                CollectPublications(jsonObj);

                if (allPublications.Count == 0)
                {
                    var name = jsonObj.SelectToken("name")?.ToString() ?? "<unknown file>";
                    Console.WriteLine($"Rejected file (no publication fields): {name}");
                    return false;
                }

                // At least one publication must have a valid title from sourceBooks
                bool hasValidTitle = allPublications.Any(pub =>
                    pub["title"] != null && Constants.sourceBooks.Contains(pub["title"].ToString())
                );

                // All publications must have valid license and remaster
                bool allValidLicenseRemaster = allPublications.All(pub =>
                    IsValidLicense(pub["license"]?.ToString()) &&
                    IsValidRemaster(pub)
                );

                bool approved = hasValidTitle && allValidLicenseRemaster;

                if (!approved)
                {
                    var name = jsonObj.SelectToken("name")?.ToString() ?? "<unknown file>";
                    Console.WriteLine($"Rejected file (publication criteria not met): {name}, hasValidTitle: {hasValidTitle}, allValidLicenseRemaster: {allValidLicenseRemaster}");
                }
                return approved;
            }
            catch
            {
                Console.WriteLine("Rejected file (invalid JSON or missing publication field)");
                return false;
            }
        }   
    }
}