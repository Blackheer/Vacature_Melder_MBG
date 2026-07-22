import json
import os
import smtplib
from email.message import EmailMessage
import requests
from bs4 import BeautifulSoup

# De exacte vacaturepagina van Cogas
URL = "https://werkenbij.cogas.nl/vacatures?functiegroep="
JSON_FILE = "vacancies.json"

def get_current_vacancies():
    """Haalt de Cogas-pagina op en zoekt specifiek naar de vacatureblokken."""
    headers = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
    try:
        response = requests.get(URL, headers=headers)
        response.raise_for_status()
    except Exception as e:
        print(f"Fout bij ophalen pagina: {e}")
        return []

    soup = BeautifulSoup(response.text, 'html.parser')
    vacancies = []

    # We zoeken naar alle links op de pagina die naar een vacature wijzen (/vacature/)
    for item in soup.find_all('a', href=True):
        link = item['href']
        
        # Check of het echt om een vacaturelink gaat
        if "/vacature/" in link:
            title = item.get_text(strip=True)
            
            # Soms pakt een link per ongeluk het pijltje of de hele tegel; 
            # we zorgen dat we een geldige functietitel te pakken hebben.
            if title and len(title) > 3:
                # Maak de link compleet als deze begint met een schuine streep
                if link.startswith('/'):
                    link = "https://werkenbij.cogas.nl" + link
                
                # Voeg toe aan de lijst als unieke combinatie
                job_entry = {"title": title, "link": link}
                if job_entry not in vacancies:
                    vacancies.append(job_entry)
                
    return vacancies

def send_notification(new_vacancies):
    """Verstuurt een e-mail via Gmail als er nieuwe vacatures zijn."""
    sender_email = os.environ.get("MAIL_USERNAME")
    sender_password = os.environ.get("MAIL_PASSWORD")
    receiver_email = os.environ.get("MAIL_TO")

    if not sender_email or not sender_password or not receiver_email:
        print("E-mailconfiguratie ontbreekt in GitHub Secrets.")
        return

    # Maak het e-mailbericht klaar
    msg = EmailMessage()
    msg['Subject'] = f"🚨 Nieuwe Cogas vacature(s) gevonden! ({len(new_vacancies)})"
    msg['From'] = sender_email
    msg['To'] = receiver_email

    # Zet alle nieuwe vacatures onder elkaar in de mail
    body = "Er zijn nieuwe vacatures gevonden bij Cogas:\n\n"
    for v in new_vacancies:
        body += f"- {v['title']}\n  {v['link']}\n\n"

    msg.set_content(body)

    # Log in op Gmail en verstuur de e-mail
    try:
        with smtplib.SMTP_SSL('smtp.gmail.com', 465) as smtp:
            smtp.login(sender_email, sender_password)
            smtp.send_message(msg)
        print("E-mail succesvol verzonden!")
    except Exception as e:
        print(f"Fout bij verzenden e-mail: {e}")

def main():
    """Hoofdfunctie: Vergelijkt de website met de opgeslagen lijst."""
    current_vacancies = get_current_vacancies()
    if not current_vacancies:
        print("Geen vacatures gevonden op de pagina.")
        return

    # Laad de oude lijst uit het JSON-bestand
    old_vacancies = []
    if os.path.exists(JSON_FILE):
        with open(JSON_FILE, "r", encoding="utf-8") as f:
            try:
                old_vacancies = json.load(f)
            except json.JSONDecodeError:
                old_vacancies = []

    # Vergelijk de huidige vacatures met de opgeslagen oude lijst op basis van de link
    old_links = {v['link'] for v in old_vacancies}
    new_vacancies = [v for v in current_vacancies if v['link'] not in old_links]

    # Als er nieuwe vacatures bij zijn gekomen: stuur een mail en update het JSON-bestand
    if new_vacancies:
        print(f"Er zijn {len(new_vacancies)} nieuwe vacatures!")
        send_notification(new_vacancies)
        
        with open(JSON_FILE, "w", encoding="utf-8") as f:
            json.dump(current_vacancies, f, indent=4, ensure_ascii=False)
    else:
        print("Geen nieuwe vacatures gevonden.")

if __name__ == "__main__":
    main()
