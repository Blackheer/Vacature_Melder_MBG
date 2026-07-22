// ============================================================
// Cogas Vacature Melder
// Dit programma controleert dagelijks of er nieuwe vacatures
// zijn op werkenbij.cogas.nl en stuurt een e-mail als dat zo is.
// ============================================================

using System.Net.Mail;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Playwright;

// ── Instellingen ─────────────────────────────────────────────
string vacaturePagina = "https://werkenbij.cogas.nl/vacatures";
string opslagBestand  = "vacancies.json";
// ─────────────────────────────────────────────────────────────

Console.WriteLine("=== Cogas Vacature Melder gestart ===");

// Stap 1: haal alle huidige vacatures op van de website
var gevondenVacatures = await HaalVacaturesOpVanWebsite();

if (gevondenVacatures.Count == 0)
{
    Console.WriteLine("Geen vacatures gevonden op de website. Programma stopt.");
    return;
}

Console.WriteLine($"{gevondenVacatures.Count} vacature(s) gevonden op de website.");

// Stap 2: lees de eerder opgeslagen vacatures uit het JSON-bestand
var oudeVacatures = LeesOpgeslagenVacatures();

// Stap 3: vergelijk — welke vacatures zijn er nieuw bijgekomen?
var oudeVacatureTitels = oudeVacatures.Select(v => v.Titel).ToHashSet();
var nieuweVacatures    = gevondenVacatures.Where(v => !oudeVacatureTitels.Contains(v.Titel)).ToList();

// Stap 4: stuur een e-mail als er nieuwe vacatures zijn
if (nieuweVacatures.Count > 0)
{
    Console.WriteLine($"Goed nieuws: {nieuweVacatures.Count} nieuwe vacature(s) gevonden!");
    StuurEmail(nieuweVacatures);
}
else
{
    Console.WriteLine("Geen nieuwe vacatures sinds de laatste controle.");
}

// Stap 5: sla de huidige vacatures op voor de volgende keer
SlaVacaturesOp(gevondenVacatures);

Console.WriteLine("=== Klaar ===");


// ════════════════════════════════════════════════════════════
// FUNCTIES
// ════════════════════════════════════════════════════════════

// Opent de Cogas-website met een echte browser (Playwright),
// leest alle vacaturetitels uit en klikt op elke kaart
// om de directe link naar de vacature te achterhalen.
async Task<List<Vacature>> HaalVacaturesOpVanWebsite()
{
    var resultaat = new List<Vacature>();

    try
    {
        // Start een onzichtbare browser op de achtergrond
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true }
        );

        // --- Overzichtspagina laden en titels verzamelen ---
        Console.WriteLine("Website laden...");
        var overzichtsPagina = await browser.NewPageAsync();
        await overzichtsPagina.GotoAsync(vacaturePagina, new PageGotoOptions
        {
            Timeout    = 30_000,              // maximaal 30 seconden wachten
            WaitUntil  = WaitUntilState.NetworkIdle  // wacht tot de pagina klaar is
        });

        // De vacaturetitels staan in <h4> elementen op de pagina
        var titelElementen = await overzichtsPagina.QuerySelectorAllAsync("h4");
        var titels = new List<string>();

        foreach (var element in titelElementen)
        {
            string titel = (await element.InnerTextAsync())?.Trim() ?? "";
            if (titel.Length > 3)
                titels.Add(titel);
        }

        Console.WriteLine($"{titels.Count} vacaturetitel(s) gevonden. Links ophalen...");
        await overzichtsPagina.CloseAsync();

        // --- Per vacature: klik op de kaart en vang de URL op ---
        foreach (var titel in titels)
        {
            try
            {
                // Open een nieuwe browsertab voor elke vacature
                var detailPagina = await browser.NewPageAsync();

                // Ga terug naar het overzicht
                await detailPagina.GotoAsync(vacaturePagina, new PageGotoOptions
                {
                    Timeout   = 30_000,
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                // Zoek de vacaturekaart met deze titel en klik erop
                var kaart = await detailPagina.QuerySelectorAsync($"h4 >> text=\"{titel}\"");

                if (kaart == null)
                {
                    // Kaart niet gevonden — voeg toch toe met de overzichtslink als fallback
                    Console.WriteLine($"  Kaart niet klikbaar: {titel}");
                    resultaat.Add(new Vacature { Titel = titel, Link = vacaturePagina });
                    await detailPagina.CloseAsync();
                    continue;
                }

                // Klik op de kaart en wacht tot de nieuwe pagina geladen is
                await Task.WhenAll(
                    detailPagina.WaitForNavigationAsync(new PageWaitForNavigationOptions
                    {
                        Timeout   = 15_000,
                        WaitUntil = WaitUntilState.NetworkIdle
                    }),
                    kaart.ClickAsync()
                );

                // De URL van de pagina waar we nu op staan = de vacaturelink
                string link = detailPagina.Url;
                Console.WriteLine($"  Gevonden: {titel}");
                Console.WriteLine($"            {link}");

                resultaat.Add(new Vacature { Titel = titel, Link = link });
                await detailPagina.CloseAsync();
            }
            catch (Exception fout)
            {
                Console.WriteLine($"  Fout bij '{titel}': {fout.Message}");
                resultaat.Add(new Vacature { Titel = titel, Link = vacaturePagina });
            }
        }
    }
    catch (Exception fout)
    {
        Console.WriteLine($"Fout bij het laden van de website: {fout.Message}");
    }

    return resultaat;
}


// Leest het JSON-bestand met eerder gevonden vacatures.
// Als het bestand nog niet bestaat, geeft het een lege lijst terug.
List<Vacature> LeesOpgeslagenVacatures()
{
    if (!File.Exists(opslagBestand))
    {
        Console.WriteLine("Geen eerder opgeslagen vacatures gevonden (eerste keer).");
        return new List<Vacature>();
    }

    try
    {
        string inhoud = File.ReadAllText(opslagBestand);
        return JsonConvert.DeserializeObject<List<Vacature>>(inhoud) ?? new List<Vacature>();
    }
    catch (Exception fout)
    {
        Console.WriteLine($"Fout bij lezen van opgeslagen vacatures: {fout.Message}");
        return new List<Vacature>();
    }
}


// Slaat de huidige vacaturelijst op als JSON-bestand,
// zodat we de volgende keer kunnen vergelijken.
void SlaVacaturesOp(List<Vacature> vacatures)
{
    string jsonInhoud = JsonConvert.SerializeObject(vacatures, Formatting.Indented);
    File.WriteAllText(opslagBestand, jsonInhoud);
    Console.WriteLine($"Vacatures opgeslagen in {opslagBestand}.");
}


// Stuurt een e-mail met een overzicht van de nieuwe vacatures.
// De e-mailinstellingen worden gelezen uit omgevingsvariabelen
// zodat wachtwoorden niet in de code staan.
void StuurEmail(List<Vacature> nieuweVacatures)
{
    // Lees de e-mailinstellingen uit de omgevingsvariabelen
    string? afzender   = Environment.GetEnvironmentVariable("MAIL_USERNAME");
    string? wachtwoord = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
    string? ontvanger  = Environment.GetEnvironmentVariable("MAIL_TO");

    if (string.IsNullOrEmpty(afzender) || string.IsNullOrEmpty(wachtwoord) || string.IsNullOrEmpty(ontvanger))
    {
        Console.WriteLine("E-mailinstellingen ontbreken. Stel MAIL_USERNAME, MAIL_PASSWORD en MAIL_TO in.");
        return;
    }

    try
    {
        // Bouw de lijst met vacatures op als klikbare HTML-links
        string vacatureLijst = string.Join("\n", nieuweVacatures.Select(v =>
            $"  <li><a href=\"{v.Link}\">{System.Net.WebUtility.HtmlEncode(v.Titel)}</a></li>"
        ));

        string aantalText = nieuweVacatures.Count == 1 ? "is 1 nieuwe vacature" : $"zijn {nieuweVacatures.Count} nieuwe vacatures";

        string emailInhoud = $"""
            <html>
            <body style="font-family: Arial, sans-serif; color: #222;">
              <h2>Nieuwe vacature(s) bij Cogas!</h2>
              <p>Er {aantalText} beschikbaar:</p>
              <ul>
            {vacatureLijst}
              </ul>
              <p style="color: #888; font-size: 0.9em;">
                Bekijk alle vacatures op
                <a href="https://werkenbij.cogas.nl/vacatures">werkenbij.cogas.nl</a>.
              </p>
            </body>
            </html>
            """;

        // Stel de e-mail in
        var email = new MailMessage
        {
            From       = new MailAddress(afzender, "Cogas Vacature Melder"),
            Subject    = $"Nieuwe Cogas vacature(s)! ({nieuweVacatures.Count})",
            Body       = emailInhoud,
            IsBodyHtml = true
        };
        email.To.Add(ontvanger);

        // Verstuur via Gmail
        using var smtp = new SmtpClient("smtp.gmail.com")
        {
            Port        = 587,
            Credentials = new NetworkCredential(afzender, wachtwoord),
            EnableSsl   = true
        };

        smtp.Send(email);
        Console.WriteLine("E-mail verstuurd!");
    }
    catch (Exception fout)
    {
        Console.WriteLine($"Fout bij versturen e-mail: {fout.Message}");
    }
}


// ════════════════════════════════════════════════════════════
// DATAMODEL
// Een vacature heeft een titel (naam van de baan) en een link
// (de URL naar de vacaturepagina op de Cogas-website).
// ════════════════════════════════════════════════════════════
public class Vacature
{
    [JsonProperty("titel")]
    public string Titel { get; set; } = string.Empty;

    [JsonProperty("link")]
    public string Link { get; set; } = string.Empty;
}