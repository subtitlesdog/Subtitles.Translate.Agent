<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 Introduction au Projet
**Moteur de Sous-titres Intelligent Disruptif**. Basé sur la technologie de **Collaboration Multi-Agents**, il automatise l'ensemble du processus, livrant des travaux de traduction de **niveau quasi-humain**.

### ✨ Points Forts
- **Style Unifié** : Génère d'abord un guide de style pour tout le film (ton/style/stratégie d'adresse), évitant la sensation de "deux traducteurs différents".
- **Terminologie Cohérente** : Génère et applique automatiquement un glossaire, unifiant les noms/lieux/noms propres et le genre des pronoms (il/elle).
- **Désambiguïsation du Contexte** : Utilise une fenêtre glissante pour référencer les traductions précédentes + les aperçus futurs, réduisant les références ambiguës et les erreurs de segmentation de phrases.
- **Audit Sémantique** : Boucle de révision intégrée vérifiant spécifiquement les erreurs de traduction/omissions/hallucinations ; respecte également les protocoles d'enjambement, ne complétant pas aléatoirement des demi-phrases.
- **Plus Humain** : Enjambement + traduction glissante multi-lignes, rendant le ton plus cohérent et réduisant la "saveur de traduction automatique".
- **Sous-titres Multi-Formats** : Prend en charge plusieurs formats d'entrée de sous-titres courants (détection automatique).
- **Économie de Tokens** : Format de sous-titres compact, réduisant la consommation de Tokens et améliorant l'efficacité du traitement.


### Démarrage Rapide

#### Exigences de l'Environnement
- .NET SDK 10.0 (Le TargetFramework de ce projet est net10.0)
- Clé API LLM et Endpoint disponibles (personnalisable dans l'entrée de démarrage)

#### Exécuter
Exécutez dans le répertoire racine du projet :

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### Utilisation Interactive
Après le démarrage, le programme demandera séquentiellement :
- Entrez le chemin local du fichier de sous-titres (supporte le glisser-déposer vers le terminal)
- Entrez la langue cible (Entrée par défaut : Chinois Simplifié)
- Entrez la Clé API (demande si non configurée)

#### Fichiers de Sortie
- Après la traduction, il sera généré dans le même répertoire que le sous-titre original : `NomFichierOriginal.<langueCible>.srt`
- Par défaut écrit une traduction monolingue (`GenerateTranslatedSrt()`) ; pour des sous-titres bilingues, changez le point d'entrée en `GenerateBilingualSrt()`

#### Modèles et Endpoints Personnalisés
La configuration d'entrée se trouve à l'initialisation de `AgentSystemConfig` dans [Program.cs:L90-L105](src/Subtitles.Translate.Agent/Program.cs#L90-L105), où `ModelId`, `Endpoint`, `ApiKey`, etc. peuvent être modifiés.

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

Exemple de modification (utilisant des modèles à contexte long pour Agent 1/2, modèle à moindre coût pour la traduction) :

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

#### Modèles Recommandés (Compromis Coût/Contexte)
- Agent 1 (Directeur) / Agent 2 (Glossaire) : Recommande `gemini-3-flash` (contexte plus long, coût plus bas, meilleur pour le scan global et l'extraction de terminologie)
- Étape de Traduction (Traducteur) : Peut utiliser des modèles à moindre coût, par ex., `gpt-oss-120b`



### 1. Step1_DirectorAgent (Compréhension Globale / Guide de Style)
- **Ce qu'il fait** : Lit d'abord le sous-titre complet, générant un "guide de film complet" que les traductions ultérieures doivent suivre strictement, résolvant la dérive de style et la confusion de traitement.
- **Entrée** : Sous-titres complets (formatés compacts), langue cible et autres paramètres de requête.

### 2. Step2_GlossaryAgent (Extraction de Terminologie / Contrainte de Cohérence)
- **Ce qu'il fait** : Basé sur la stratégie de l'Étape 1 et les sous-titres complets, extrait les entités clés et construit un glossaire contrôlé, assurant "un nom, une traduction".
- **Entrée** : Guide de Style de l'Étape 1 + Sous-titres Complets.
- **Sortie** : Table des personnages (y compris alias et inférence de genre), table des lieux, table de terminologie (y compris domaine et définition), et fournit une version Markdown directement intégrable dans les prompts.

### 3. Step3_TranslatorAgent (Traduction à Fenêtre Glissante / Validation Forte de Format)
- **Ce qu'il fait** : Traduit les sous-titres par lots en utilisant une fenêtre glissante, référençant le contexte précédent et les aperçus futurs pour réduire les erreurs de segmentation, de référence et d'ambiguïté.
- **Entrée** : Guide de Style de l'Étape 1 + Glossaire de l'Étape 2 + Contexte Traduit Précédent + Lot Actuel + Aperçu Futur.
- **Sortie** : Traduction initiale ligne par ligne (forcée de garder les ID originaux et la quantité cohérente) ; déclenche optionnellement l'Étape 4 pour audit sémantique avant d'écrire le brouillon final.

### 4. Step4_ReviewerAgent [À Venir Open Source] (Audit Sémantique / Protocole de Rétro-traduction)
- **Ce qu'il fait** : Effectue uniquement des corrections de "niveau audit" pour la précision sémantique, vérifiant spécifiquement les erreurs de traduction, omissions et hallucinations ; pas de polissage, pas d'embellissement de terminologie.
- **Entrée** : Lot de Traduction Initiale de l'Étape 3.
- **Sortie** : PASS/FIXED ligne par ligne, raison de l'erreur (critique) et traduction finale adoptée (final_translation), strictement alignée avec la quantité de l'Étape 3.

### 5. Step5_PolisherAgent [À Venir Open Source] (Conformité Terminologique + Polissage de Flux)
- **Ce qu'il fait** : Sans briser les points de coupure de la chronologie, impose d'abord les corrections de terminologie et de pronoms, puis effectue un polissage d'expression plus natif et une optimisation du rythme.
- **Entrée** : Glossaire de l'Étape 2 + Guide de Style de l'Étape 1 + Traduction du Lot Actuel + Résultat Poli Précédent (pour une transition cohérente).
- **Sortie** : polished_text, note optionnelle (explication argot/terminologie), optimization_tag (correction terminologie/polissage contexte/adaptation style/pas de changement).

### 6. Step6_TimingAdjusterAgent [À Venir Open Source] (Ajustement Fin de la Chronologie pour Confort de Lecture)
- **Ce qu'il fait** : Étend automatiquement end_time basé sur la longueur de la traduction et l'heure de début de la phrase suivante pour améliorer la lisibilité ; change uniquement l'heure de fin, ne touche pas à l'heure de début, chevauchement non autorisé.
- **Entrée** : Texte traduit, début/fin original, début de la phrase suivante (y compris tampon de sécurité de 50ms).
- **Sortie** : KEEP/EXTEND, adjusted_end, raison, et applique les ajustements à l'objet sous-titre.

## 📅 Plan Open Source
- **Février 2026** : Ouvrir Step6_TimingAdjusterAgent
- **Mars 2026** : Développer UI Windows / macOS / Web

## 🙏 Remerciements

Ce projet utilise les excellents projets open-source suivants :

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse) : Puissante bibliothèque centrale d'édition et de traitement de sous-titres.
- **[Microsoft Agents](https://github.com/microsoft/agents)** : Cadre de base pour construire des Agents intelligents.
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)** : Fournit le support .NET pour les modèles Google Gemini.
