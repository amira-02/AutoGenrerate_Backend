# AutoPost API

Résumé
----
API .NET 8 pour la gestion d'authentification/OTP. Fournit des contrôleurs exposés via Swagger. Configuration par `appsettings.json`.

Prérequis
----
- .NET 8 SDK
- SQL Server (ou LocalDB) accessible depuis la machine
- Outil EF CLI (optionnel mais recommandé) : `dotnet-ef`
- Visual Studio 2026 (optionnel)

Installation des dépendances
----
Depuis le dossier du projet :

````````

Configuration
----
- Fichier de config : `appsettings.json`
- Chaîne de connexion par défaut attendue : clé `ConnectionStrings:DefaultConnection`

Exemples de chaînes de connexion
- LocalDB (dev Windows) :
	
````````
- SQL Server Express :
````````
- SQL Server distant (SQL Auth) :


Créer la base de données (migrations EF Core)
----
1. Créer la migration :

````````
2. Appliquer la migration / créer la DB :
````````

````````

Remarques :
- Exécuter ces commandes depuis le répertoire contenant le `.csproj`, ou utiliser `--project` / `--startup-project`.
- Si vous utilisez LocalDB, assurez-vous qu'elle est démarrée (`sqllocaldb i` / `sqllocaldb start MSSQLLocalDB`).

Exécution du projet
----
- Visual Studio 2026 : F5 (ou Ctrl+F5) — choisissez le profil `https` ou `http` dans les profils de lancement.
- CLI :
   dotnet run