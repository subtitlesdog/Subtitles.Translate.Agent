<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 Projekteinführung
**Disruptive Intelligente Untertitel-Engine**. Basierend auf **Multi-Agenten-Kollaborationstechnologie**, automatisiert sie den gesamten Prozess und liefert Übersetzungsarbeiten auf **nahezu menschlichem Niveau**.

### ✨ Highlights
- **Einheitlicher Stil**: Generiert zuerst einen Stilguide für den gesamten Film (Ton/Stil/Ansprachestrategie), um das Gefühl von "zwei verschiedenen Übersetzern" zu vermeiden.
- **Konsistente Terminologie**: Generiert und erzwingt automatisch ein Glossar, vereinheitlicht Namen/Orte/Eigennamen und Pronomen-Geschlecht (er/sie).
- **Kontext-Disambiguierung**: Verwendet ein gleitendes Fenster, um auf vorherige Übersetzungen + zukünftige Vorschauen zu verweisen, wodurch mehrdeutige Referenzen und Satzsegmentierungsfehler reduziert werden.
- **Semantische Prüfung**: Eingebaute Überprüfungsschleife speziell zur Kontrolle auf Übersetzungsfehler/Auslassungen/Halluzinationen; hält sich auch an Enjambement-Protokolle und vervollständigt keine Halbsätze zufällig.
- **Menschlicher**: Enjambement + mehrzeilige gleitende Übersetzung, macht den Ton kohärenter und reduziert den "Maschinenübersetzungs-Geschmack".
- **Multi-Format Untertitel**: Unterstützt mehrere gängige Untertitel-Eingabeformate (automatische Erkennung).
- **Token-Sparen**: Kompaktes Untertitelformat, reduziert den Token-Verbrauch und verbessert die Verarbeitungseffizienz.


### Schnellstart

#### Systemanforderungen
- .NET SDK 10.0 (Dieses Projekt TargetFramework ist net10.0)
- Verfügbarer LLM API Key und Endpoint (anpassbar im Starteintrag)

#### Ausführen
Im Projektstammverzeichnis ausführen:

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### Interaktive Nutzung
Nach dem Start fordert das Programm nacheinander auf:
- Lokalen Pfad der Untertiteldatei eingeben (unterstützt Drag & Drop ins Terminal)
- Zielsprache eingeben (Enter standardmäßig: Vereinfachtes Chinesisch)
- API Key eingeben (fragt, wenn nicht konfiguriert)

#### Ausgabedateien
- Nach der Übersetzung wird im selben Verzeichnis wie der Originaluntertitel generiert: `OriginalDateiname.<zielsprache>.srt`
- Standardmäßig wird eine einsprachige Übersetzung geschrieben (`GenerateTranslatedSrt()`); für zweisprachige Untertitel ändern Sie den Einstiegspunkt zu `GenerateBilingualSrt()`

#### Benutzerdefinierte Modelle und Endpoints
Die Einstiegskonfiguration befindet sich bei der Initialisierung von `AgentSystemConfig` in [Program.cs:L90-L105](src/Subtitles.Translate.Agent/Program.cs#L90-L105), wo `ModelId`, `Endpoint`, `ApiKey` usw. geändert werden können.

```csharp
// src/Subtitles.Translate.Agent/Program.cs
var systemConfig = new AgentSystemConfig();
systemConfig.AddDefaultConfig(new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

Änderungsbeispiel (Verwendung von Long-Context-Modellen für Agent 1/2, kostengünstigeres Modell für Übersetzung):

```csharp
var systemConfig = new AgentSystemConfig();
systemConfig.AddConfig(nameof(Step1_DirectorAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step2_GlossaryAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step3_TranslatorAgent), new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

#### Empfohlene Modelle (Kosten/Kontext-Abwägung)
- Agent 1 (Regisseur) / Agent 2 (Glossar): Empfehle `gemini-3-flash` (längerer Kontext, geringere Kosten, besser für globales Scannen und Terminologieextraktion)
- Übersetzungsphase (Übersetzer): Kann kostengünstigere Modelle verwenden, z.B. `gpt-oss-120b`



### 1. Step1_DirectorAgent (Globales Verständnis / Stilguide)
- **Was es tut**: Liest zuerst den gesamten Untertitel und generiert ein "Gesamtfilm-Handbuch", das nachfolgende Übersetzungen streng befolgen müssen, um Stildrift und Anspracheverwirrung zu lösen.
- **Eingabe**: Vollständige Untertitel (kompakt formatiert), Zielsprache und andere Anfrageparameter.

### 2. Step2_GlossaryAgent (Terminologieextraktion / Konsistenzeinschränkung)
- **Was es tut**: Basierend auf der Strategie von Schritt 1 und vollständigen Untertiteln, extrahiert Schlüsseleinheiten und erstellt ein kontrolliertes Glossar, das "ein Substantiv, eine Übersetzung" sicherstellt.
- **Eingabe**: Schritt 1 Stilguide + Vollständige Untertitel.
- **Ausgabe**: Charaktertabelle (einschließlich Alias und Geschlechtsfolgerung), Ortstabelle, Terminologietabelle (einschließlich Bereich und Definition) und stellt eine Markdown-Version bereit, die direkt in Prompts eingebettet werden kann.

### 3. Step3_TranslatorAgent (Gleitfenster-Übersetzung / Starke Formatvalidierung)
- **Was es tut**: Übersetzt Untertitel in Chargen mit einem gleitenden Fenster, verweist auf vorherigen Kontext und zukünftige Vorschauen, um Segmentierungs-, Referenz- und Mehrdeutigkeitsfehler zu reduzieren.
- **Eingabe**: Schritt 1 Stilguide + Schritt 2 Glossar + Vorheriger übersetzter Kontext + Aktuelle Charge + Zukünftige Vorschau.
- **Ausgabe**: Zeilenweise Erstübersetzung (gezwungen, ursprüngliche IDs und Menge konsistent zu halten); löst optional Schritt 4 für semantische Prüfung aus, bevor der endgültige Entwurf geschrieben wird.

### 4. Step4_ReviewerAgent [Wird Open Source] (Semantische Prüfung / Rückübersetzungsprotokoll)
- **Was es tut**: Führt nur Korrekturen auf "Prüfungsebene" für semantische Genauigkeit durch, prüft speziell auf Übersetzungsfehler, Auslassungen und Halluzinationen; kein Polieren, keine Terminologieverschönerung.
- **Eingabe**: Schritt 3 Erstübersetzungscharge.
- **Ausgabe**: Zeilenweise PASS/FIXED, Fehlergrund (Kritik) und endgültig angenommene Übersetzung (final_translation), streng an Schritt 3 Menge ausgerichtet.

### 5. Step5_PolisherAgent [Wird Open Source] (Terminologie-Einhaltung + Fluss-Polieren)
- **Was es tut**: Ohne Zeitachsen-Schnittpunkte zu brechen, erzwingt zuerst Terminologie- und Pronomenkorrekturen, führt dann ein nativeres Ausdruckspolieren und Rhythmusoptimierung durch.
- **Eingabe**: Schritt 2 Glossar + Schritt 1 Stilguide + Aktuelle Chargenübersetzung + Vorheriges Polierergebnis (für kohärenten Übergang).
- **Ausgabe**: polished_text, optionale Notiz (Slang/Terminologieerklärung), optimization_tag (Terminologiekorrektur/Kontextpolieren/Stilanpassung/keine Änderung).

### 6. Step6_TimingAdjusterAgent [Wird Open Source] (Zeitachsen-Feinabstimmung für Lesekomfort)
- **Was es tut**: Verlängert automatisch end_time basierend auf Übersetzungslänge und Startzeit des nächsten Satzes, um die Lesbarkeit zu verbessern; ändert nur die Endzeit, berührt nicht die Startzeit, Überlappung nicht erlaubt.
- **Eingabe**: Übersetzter Text, Original Start/Ende, Nächster Satz Start (einschließlich 50ms Sicherheitspuffer).
- **Ausgabe**: KEEP/EXTEND, adjusted_end, Grund, und wendet Anpassungen auf das Untertitelobjekt an.

## 📅 Open Source Plan
- **Februar 2026**: Öffne Step6_TimingAdjusterAgent
- **März 2026**: Entwickle Windows / macOS / Web UI

## 🙏 Danksagungen

Dieses Projekt verwendet die folgenden hervorragenden Open-Source-Projekte:

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse): Leistungsstarke Kernbibliothek für Untertitelbearbeitung und -verarbeitung.
- **[Microsoft Agents](https://github.com/microsoft/agents)**: Grundlegendes Framework zum Erstellen intelligenter Agenten.
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)**: Bietet .NET-Unterstützung für Google Gemini-Modelle.
