// Détection best-effort du pays (Gabon/Maroc) via le GPS du navigateur, pour
// présélectionner le canal de transfert pertinent — sans aucun appel API externe :
// simple test de boîte englobante (les deux pays sont très éloignés, pas besoin
// de géocodage précis). Retourne "GABON" | "MAROC" | null (refusé/indisponible/
// hors zone) — n'échoue jamais bruyamment, la page garde son comportement par défaut.
window.adnGeoloc = {
    detecterPays: function () {
        return new Promise(function (resolve) {
            if (!navigator.geolocation) { resolve(null); return; }
            navigator.geolocation.getCurrentPosition(
                function (pos) {
                    var lat = pos.coords.latitude, lng = pos.coords.longitude;
                    if (lat >= -4 && lat <= 2.5 && lng >= 8 && lng <= 15) resolve('GABON');
                    else if (lat >= 27 && lat <= 36 && lng >= -14 && lng <= -1) resolve('MAROC');
                    else resolve(null);
                },
                function () { resolve(null); }, // permission refusée, timeout, etc.
                { timeout: 4000, maximumAge: 600000 }
            );
        });
    }
};
