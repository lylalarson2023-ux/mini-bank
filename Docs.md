# 🏦 ADN_pay — Manifeste projet

## 🌌 Vision
Néo-banque pour étudiants à Casablanca. Style : sobre, géométrique, dégradé de marque
`#0099CC → #30B880`. App live : **https://adnpay.net**.

---

## 🏗️ Architecture en couches
Refactor monolithe Blazor → couches propres (sens des dépendances : Domain ← Infrastructure ← Application ← Web/Admin, aucun cycle).

- **`ADN.Domain`** — entités (`UserProfile`, `Transaction`, `SavingsPocket`…), règles pures, `Result`, `MoneyExtensions`, `PiiMasker`. 0 dépendance.
- **`ADN.Infrastructure`** — EF Core, `BankDbContext`, migrations (SQLite).
- **`ADN.Application`** — services métier (`Account`, `Admin`, `Credit`, `Savings`, `TwoFactor`, `NotificationHistory`), `UserContext`, `IEmailSender`.
- **`ADN_pay`** (web) — Blazor Server + glue ASP.NET (`AuthService`, `BankService`, `FileService`, Stripe, e-mail). Écoute `:5163` en dev, `:5000` en prod (service).
- **`ADN.Admin`** — app Blazor Server **séparée** (admin cookie + API REST JWT fusionnées), port `:5200`. Dépend de Domain/Infra/Application uniquement.
- **`ADN_pay.Tests`** — tests xUnit.

> Astuce build : chaque sous-projet sous `ADN_pay/` est exclu du glob du web
> (`<Compile Remove="ADN.Xxx\**\*.cs" />` dans `ADN_pay.csproj`).

---

## 💳 Grille tarifaire (source de vérité)

### 1. Types de prêts
| Catégorie | Frais fixes | Intérêt (base) | Durée limite |
| :-------- | :---------- | :------------- | :----------- |
| DÉPANNAGE | 5 DH        | 10 %           | 7 jours      |
| ÉTUDES    | 20 DH       | 12 %           | 60 jours     |
| URGENCE   | 20 DH       | 15 %           | 45 jours     |

### 2. Statuts & dossiers (upgrades)
- **STANDARD (0 DH)** : réduction **-0 %** sur les taux.
- **PREMIUM (100 DH)** : réduction **-2 %** sur les taux.

### 3. Bonus de fidélité
- **Actif (> 50 transactions)** : réduction supplémentaire **-1 %**.

---

## 🚀 Roadmap
- [x] Refactor en couches (Domain / Infrastructure / Application / Web).
- [x] API REST + JWT (mobile / partenaires) — fusionnée dans ADN.Admin.
- [x] Zone admin Blazor séparée (KYC, crédits, dépôt, utilisateurs, tuteurs, logs, scoring, transactions).
- [ ] Durcir + déployer l'admin/API (secrets prod, rate-limit login, services Windows + sous-domaines).
- [ ] Décommissionner l'ancien onglet admin du web (`Admin.razor`).
- [ ] Migrer SQLite → PostgreSQL (multi-écrivains) avant le mobile / vrai trafic.

---

## 🛠️ Conventions
- **Argent en centimes** (`long`) partout (ADR-001) ; affichage via `MoneyExtensions.ToDh()`.
- Culture **fr-FR** pour le formatage.
- Vérifier `dotnet build` + `dotnet test` (19/19) avant d'ajouter une feature.
- Déploiement : voir `DEPLOIEMENT.md` (racine du dépôt parent).
