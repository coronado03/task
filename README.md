# TransactionsUsdConverter
INFO ON TASK WILL BE SKIPPED FOR PRIVACY OF PROVIDER.

A .NET console app that connects to an API, reads transactions in various currencies, converts them to USD using the site's exchange rates, and uploads the results.

## What happened with the API

The test site at was returning **501 Not Implemented** on every request during the task window. Here's what I tried to get it working:

- Common Swagger paths: `/swagger`, `/swagger/index.html`, `/swagger/v1/swagger.json`, `/api-docs`
- Authentication endpoints: `/api/auth/login`, `/api/login`, `/auth/login`, `/login` (POST with JSON credentials)
- Different HTTP methods: GET, POST, OPTIONS
- Various headers: `Accept: application/json`, `Authorization: Bearer`, `X-API-Key`
- Alternative ports: 8443 (got 522 timeout), 8080, 3000, 2053, 2083, 2096
- Subdomains: `api.devtest.qa.track360.pro`, `api.qa.track360.pro`, `qa.track360.pro`
- POSTing credentials directly to the root `/`

Everything came back 501 from nginx. The 522 on port 8443 suggested the infrastructure existed but the backend app perhaps was not running.

## How it works

The app follows five steps:

1. **Login** — POSTs email/password to the auth endpoint, gets a Bearer token
2. **Fetch transactions** — GETs the transaction list (expects JSON array with `id`, `amount`, `currency`)
3. **Fetch exchange rates** — GETs the rates (each rate has a `currency`, `rate` value, and `direction`)
4. **Convert to USD** — For each transaction:
   - If the rate direction is **"to USD"**: `usdAmount = amount × rate`
   - If the rate direction is **"from USD"**: `usdAmount = amount ÷ rate`
   - If already USD: `usdAmount = amount` (no conversion)
5. **Upload** — POSTs the enriched transactions (now including `usdAmount`) back to the API

## How to run

Make sure you have .NET 10 SDK installed, then:

```bash
dotnet run
```

## Configuration

Since I couldn't access Swagger to discover the real endpoint paths and field names, these are defined as constants at the top of `TransactionsUsdConverter.cs`:


Once the real Swagger docs are available, I can just update the urls and perhaps Schemas. The rest of the logic should work as-is.

## Design decisions

- **`JsonNode`/`JsonObject` instead of typed models** — Without Swagger I didn't know the exact response shapes. Using dynamic JSON nodes means the code adapts to whatever fields come back, and adding `usdAmount` to the existing objects is straightforward without defining rigid classes.
- **Single file** — As specified in the task requirements. Everything lives in `TransactionsUsdConverter.cs`.
- **No external packages** — Only `System.Text.Json` and `System.Net.Http` from the standard library.
- **Console logging at each step** — Makes it easy to see what's happening and debug issues when connecting to the real API.