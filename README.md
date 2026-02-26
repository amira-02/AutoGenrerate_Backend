# AutoPost API

API .NET 8 pour la gestion de l'authentification et des OTP. Fournit des contrôleurs exposés via Swagger. La configuration se fait via `appsettings.json`.

---

## Prérequis

- .NET 8 SDK  
- SQL Server ou LocalDB accessible depuis votre machine  
- Outil EF CLI (optionnel mais recommandé) : `dotnet-ef`  
- Visual Studio 2026 (optionnel)

---

## Installation des dépendances

Depuis le dossier racine du projet :

```bash
dotnet restore
Configuration

Fichier de configuration : appsettings.json

Chaîne de connexion attendue : ConnectionStrings:DefaultConnection

Exemple pour LocalDB (Windows, dev)
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AutoPostDb;Trusted_Connection=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
Exemple pour SQL Server Express
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=AutoPostDb;Trusted_Connection=True;"
  }
}
Exemple pour SQL Server distant (SQL Auth)
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-server-address;Database=AutoPostDb;User Id=your-user;Password=your-password;"
  }
}
Création de la base de données (migrations EF Core)

Créer la migration initiale :

dotnet ef migrations add InitialCreate

Appliquer la migration et créer la base de données :

dotnet ef database update

Remarques :

Exécuter ces commandes depuis le répertoire contenant le .csproj, ou utiliser les options --project et --startup-project.

Pour LocalDB, assurez-vous qu'elle est démarrée :

sqllocaldb i
sqllocaldb start MSSQLLocalDB
Exécution du projet

Visual Studio 2026 : F5 (ou Ctrl+F5) — choisissez le profil https ou http.

CLI :

dotnet run
Accès à Swagger

Une fois le projet lancé, Swagger est accessible via :

https://localhost:<port>/swagger

où <port> correspond au port configuré dans launchSettings.json.

Notes supplémentaires

Assurez-vous que vos tables sont créées avant d’essayer d’utiliser les endpoints d’authentification.

Pour tester les OTP, utilisez Swagger ou un client HTTP comme Postman.

Les migrations EF Core permettent de mettre à jour facilement la base si vous ajoutez des entités ou modifiez le modèle.