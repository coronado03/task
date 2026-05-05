using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

class TransactionsUsdConverter
{
    // MOCK ENDPOINTS
    static string BASE_URL => Env("API_BASE_URL");
    const string LOGIN_ENDPOINT        = "/api/auth/login";
    const string TRANSACTIONS_ENDPOINT = "/api/transactions";
    const string RATES_ENDPOINT        = "/api/rates";
    const string UPLOAD_ENDPOINT       = "/api/transactions";

    // Auth creds
    static string EMAIL    => Env("API_EMAIL");
    static string PASSWORD => Env("API_PASSWORD");

    // JSON field names (MOCK AS I COULDN"T ACCESS THE SWAGGER)
    const string FIELD_ID        = "id";
    const string FIELD_AMOUNT    = "amount";
    const string FIELD_CURRENCY  = "currency";
    const string FIELD_USD_AMT   = "usdAmount";
    // Rate object fields
    const string RATE_CURRENCY   = "currency"; 
    const string RATE_VALUE      = "rate";   
    const string RATE_DIRECTION  = "direction";


    static readonly HttpClient Http = new();

    static async Task Main()
    {
        Console.WriteLine("=== TransactionsUsdConverter BY SEBASTIAN CORONADO ===\n");

        string? token = await LoginAsync();
        if (token is null)
        {
            Console.WriteLine("[ABORT] Could not obtain auth token.");
            return;
        }

        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        JsonArray? transactions = await GetJsonArrayAsync("transactions", TRANSACTIONS_ENDPOINT);
        if (transactions is null)
        {
            Console.WriteLine("[ABORT] Could not retrieve transactions.");
            return;
        }
        Console.WriteLine($"[OK] Fetched {transactions.Count} transactions.\n");

        JsonArray? rates = await GetJsonArrayAsync("exchange rates", RATES_ENDPOINT);
        if (rates is null)
        {
            Console.WriteLine("[ABORT] Could not retrieve exchange rates.");
            return;
        }
        Console.WriteLine($"[OK] Fetched {rates.Count} rate entries.\n");

        var rateMap = BuildRateMap(rates);

        int converted = 0, skipped = 0;
        foreach (JsonNode? node in transactions)
        {
            if (node is not JsonObject tx) { skipped++; continue; }

            string? currency = tx[FIELD_CURRENCY]?.GetValue<string>()?.Trim().ToUpperInvariant();
            double  amount   = GetDouble(tx, FIELD_AMOUNT);
            object? txId     = tx[FIELD_ID]?.ToJsonString() ?? "?";

            if (currency == "USD")
            {
                tx[FIELD_USD_AMT] = amount;
                converted++;
                Console.WriteLine($"  tx {txId}: {amount} USD → usdAmount = {amount:F4}");
                continue;
            }

            if (currency is not null && rateMap.TryGetValue(currency, out var entry))
            {
                double usd = entry.Direction == "toUSD"
                    ? amount * entry.Rate        
                    : amount / entry.Rate;         

                tx[FIELD_USD_AMT] = usd;
                converted++;
                Console.WriteLine($"  tx {txId}: {amount} {currency} → usdAmount = {usd:F4} (rate {entry.Rate}, dir {entry.Direction})");
            }
            else
            {
                Console.WriteLine($"  tx {txId}: [SKIP] No rate found for currency '{currency}'");
                skipped++;
            }
        }
        Console.WriteLine($"\n[OK] Enriched {converted} transactions, skipped {skipped}.\n");

        await UploadTransactionsAsync(transactions);
    }

    //Login
    static async Task<string?> LoginAsync()
    {
        Console.WriteLine($"[AUTH] POST {BASE_URL}{LOGIN_ENDPOINT}");
        try
        {
            var body = JsonSerializer.Serialize(new { email = EMAIL, password = PASSWORD });
            using var request = new HttpRequestMessage(HttpMethod.Post, BASE_URL + LOGIN_ENDPOINT)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage response = await Http.SendAsync(request);
            string raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] Login failed – HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return null;
            }
            //
            JsonNode? json = JsonNode.Parse(raw);
            string? token = json?["token"]?.GetValue<string>()
                         ?? json?["accessToken"]?.GetValue<string>()
                         ?? json?["access_token"]?.GetValue<string>();

            if (token is null)
            {
                Console.WriteLine($"[ERROR] Login succeeded but no token field found. Response: {raw}");
                return null;
            }

            Console.WriteLine("[OK] Authenticated.\n");
            return token;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEPTION] LoginAsync: {ex.Message}");
            return null;
        }
    }

    static async Task<JsonArray?> GetJsonArrayAsync(string label, string endpoint)
    {
        Console.WriteLine($"[GET] {BASE_URL}{endpoint}  ({label})");
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(BASE_URL + endpoint);
            string raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] GET {label} failed – HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                Console.WriteLine($"        Body: {raw}");
                return null;
            }

            JsonNode? node = JsonNode.Parse(raw);

            if (node is JsonArray arr) return arr;

            if (node is JsonObject obj)
            {
                foreach (string key in new[] { "data", label, "items", "results", "transactions", "rates" })
                {
                    if (obj[key] is JsonArray nested) return nested;
                }
                foreach (var kv in obj)
                {
                    if (kv.Value is JsonArray found) return found;
                }
            }

            Console.WriteLine($"[ERROR] Could not locate a JSON array in the {label} response. Raw: {raw}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEPTION] GetJsonArrayAsync ({label}): {ex.Message}");
            return null;
        }
    }

    static async Task UploadTransactionsAsync(JsonArray transactions)
    {
        Console.WriteLine($"[UPLOAD] POST {BASE_URL}{UPLOAD_ENDPOINT}  ({transactions.Count} transactions)");
        try
        {
            string payload = transactions.ToJsonString();
            using var request = new HttpRequestMessage(HttpMethod.Post, BASE_URL + UPLOAD_ENDPOINT)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage response = await Http.SendAsync(request);
            string raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[WARN] POST returned HTTP {(int)response.StatusCode}. Retrying with PUT …");
                using var putRequest = new HttpRequestMessage(HttpMethod.Put, BASE_URL + UPLOAD_ENDPOINT)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage putResponse = await Http.SendAsync(putRequest);
                string putRaw = await putResponse.Content.ReadAsStringAsync();

                if (!putResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ERROR] PUT also failed – HTTP {(int)putResponse.StatusCode} {putResponse.ReasonPhrase}");
                    Console.WriteLine($"        Body: {putRaw}");
                    return;
                }

                Console.WriteLine($"[OK] Upload succeeded via PUT. Response: {putRaw}");
                return;
            }

            Console.WriteLine($"[OK] Upload succeeded. Response: {raw}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEPTION] UploadTransactionsAsync: {ex.Message}");
        }
    }

    // Helper Functions

    static Dictionary<string, (double Rate, string Direction)> BuildRateMap(JsonArray rates)
    {
        var map = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonNode? node in rates)
        {
            if (node is not JsonObject r) continue;

            string? currency = r[RATE_CURRENCY]?.GetValue<string>()?.Trim().ToUpperInvariant();
            if (currency is null) continue;

            double rateVal = GetDouble(r, RATE_VALUE);

            string rawDir  = r[RATE_DIRECTION]?.GetValue<string>() ?? "";
            string dir     = NormaliseDirection(rawDir);

            if (rateVal == 0)
            {
                Console.WriteLine($"  [WARN] Rate for {currency} is 0 – skipping.");
                continue;
            }

            map[currency] = (rateVal, dir);
        }

        return map;
    }
    static string NormaliseDirection(string raw)
    {
        string s = raw.Replace(" ", "").Replace("_", "").ToUpperInvariant();
        return s.Contains("FROM") ? "fromUSD" : "toUSD";
    }
    static double GetDouble(JsonObject obj, string key)
    {
        JsonNode? n = obj[key];
        if (n is null) return 0;
        try { return n.GetValue<double>(); } catch { }
        try { return double.Parse(n.GetValue<string>() ?? "0"); } catch { }
        return 0;
    }

    // .env loader

    static readonly Dictionary<string, string> EnvVars = LoadEnvFile();

    static string Env(string key)
    {
        if (EnvVars.TryGetValue(key, out string? val)) return val;
        return Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException(
                $"Required config key '{key}' not found. Add it to your .env file.");
    }

    static Dictionary<string, string> LoadEnvFile()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env"),
        };

        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Console.WriteLine("[WARN] No .env file found – falling back to process environment variables.");
            return new();
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;

            int eq = trimmed.IndexOf('=');
            string k = trimmed[..eq].Trim();
            string v = trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');
            if (k.Length > 0) result[k] = v;
        }

        return result;
    }
}
