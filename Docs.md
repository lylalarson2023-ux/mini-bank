# 🏦 ADN_pay - PROJECT MANIFESTO

## 🌌 Vision & Lore
Simulation bancaire futuriste se déroulant à Casablanca. 
Style : Cyberpunk / Terminal High-Tech.

---

## 🏗️ Architecture du Projet
- **`/Models`** : Définition des données (`Transaction`, `UserProfile`).
- **`/Services`** : Logique métier (`BankService`).
- **`/Components/Pages`** : Interfaces utilisateur Blazor.
- **`/wwwroot`** : Assets, CSS global.

---

## 💳 Grille Tarifaire (Source of Truth)

### 1. Types de Prêts
| Catégorie   | Frais Fixes | Intérêt (Base) | Durée Limite |
| :---------- | :---------- | :------------- | :----------- |
| DÉPANNAGE   | 5 DH        | 10%            | 7 Jours      |
| ÉTUDES      | 20 DH       | 12%            | 60 Jours     |
| URGENCE     | 20 DH       | 15%            | 45 Jours     |

### 2. Statuts & Dossiers (Upgrades)
- **STANDARD (0 DH)** : Réduction de **-0%** sur les taux.
- **PREMIUM (100 DH)** : Réduction de **-2%** sur les taux.

### 3. Bonus de Fidélité (Récurrence)
- **Actif (> 50 transactions)** : Réduction supplémentaire de **-1%**.

---

## 🛠️ Protocole de Travail avec l'IA (Gemini)
1. **Code Complet** : Toujours fournir le fichier entier (pas de `//...`).
2. **Respect des Grilles** : Toute modification de calcul doit se référer à ce document.
3. **Statut Terminal** : Vérifier que le build est OK avant d'ajouter une feature.

---

## 🚀 Roadmap (Prochaines Étapes)
- [ ] **Phase 1** : Système de pénalités automatiques en cas de retard.
- [ ] **Phase 2** : Design CSS Cyberpunk (Néons, animations de terminal).
- [ ] **Phase 3** : Gestion du temps réelle (simuler le passage des jours).
- [ ] **Phase 4** : Exportation du relevé bancaire en fichier texte.