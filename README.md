# AggregatedElevationService

## Czech
### Jak spustit službu:
1. Nainstalovat PostgreSQL společně s StackBuilder + pgAdmin
2. Pomocí StackBuilderu na databázi nainstalovat Spatial Extensions -> PostGIS Bundle
3. Vytvořit v PostgreSQL databázi s názvem "elevation_service" (pokud bude jiný název je potřeba změnit v konfiguračním souboru)
4. V konfiguračním souboru AggregatedElevationService.exe.Config vyplnit hodnoty (pokud se liší od výchozích):
    * `db_host` (default: localhost)
    * `db_port` (default: 5432)
    * `db_username` (default: postgres)
    * `db_password` (default: root)
    * `db_database` (default: elevation_service)
5.V konfiguračním souboru vyplnit Google Maps Elevation API klíč do kolonky google_api_key
6. Změnu adresy služby lze provést také v konfiguračním souboru:
    * `scheme` (default: http)
    * `host` (default: localhost)
    * `port` (default: 8889)
    * `path` (default: elevation)
7. Protože aplikace přistupuje k TCP portu, tak musí být spuštěna jako správce nebo musí být adresa, na které webová služba spuštěna, zarezervována v Network Shellu (netsh) příkazem: 
    * `netsh http add urlacl url=<schéma>://<host>:<port>/<cesta> user=<uživatel>`
8. Po první spuštění programu dojde k inicializace databáze.

### Popis služby:

Požadavek pro webovou službu bude konstruován jako URL řetězec ve formátu: 

`http://<adresa>:<port>/<cesta>/<výstupní_formát>?locations=<lokace>&key=<klíč>&source=<zdroj>`

#### Výstupní formát:
* XML
    ```
    <ElevationResponse>
        <status>OK</status>
        <result>
            <location>
                <lat>50.499805</lat>
                <lng>13.6484716</lng>
            </location>
            <elevation>297.302</elevation>
            <resolution>1.18053864</resolution>
        </result>
    </ElevationResponse>
    ```
* JSON
    ```
    {
        "result":[
        {
            "elevation":297.302,
            "location":
            {
                "lat":50.499805,
                "lng":13.6484716
            },
            "resolution":1.18053864
        }],
        "status":"OK"
    }
    ```
#### Odpověď obsahuje informace:
* status - status odpovědi, obsahuje jednu z hodnot:
    * „OK“ - obsahuje všechny výsledky
    * „KO“ - neobsahuje žádné výsledky
    * „Invalid API key“ - API klíč není platný
    * ”Results are incomplete” - všechny lokace nemají výšku
* result - jednotlivé výsledky
    * location - zadaná lokace
        * lat - zeměpisná šířka (latitude)
        * lat - zeměpisná šířka (latitude)
    * elevation - nadmořská výška v metrech
    * resolution - obsahuje vzdálenost od bodu, ze kterého byla výška získána, pokud došlo k aproximaci, tak obsahuje hodnotu vzdálenosti nejvzdálenějšího bodu, ze kterého došlo k aproximaci (pokud se jedná o výšku od Seznamu, tak je hodnota nula a pokud se jedná o hodnotu z Googlu, tak obsahuje hodnotu resolution, kterou vrací Google)

V parametru lokace jsou lokace, pro které chce uživatel získat výškopisná data. Lokace je zadána jako uspořádaná dvojice „<zeměpisná šířka>,<zeměpisná délka>“. V případě více lokací, jsou lokace rozděleny pomocí svislé čáry „|“.

Parametr klíč obsahuje API klíč identifikující uživatele používajícího službu. Uživatelé jsou rozděleni na dva druhy:
* běžný uživatel - může přistupovat pouze k výškopisným datům z veřejně dostupných zdrojů
* prémiový uživatel - může přistupovat i k výškopisným datům z neveřejných zdrojů

Parametr zdroj může obsahovat hodnoty:
* approx, která bude určovat, že se má služba pokusit aproximovat chybějící hodnoty nebo název externího výškopisu, který má použít
* google - Google Maps Elevation API
* seznam - Seznam Mapy (výchozí hodnota)
---
## English
